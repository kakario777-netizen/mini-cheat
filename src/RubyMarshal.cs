using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace MiniCheat
{
    // ───────── 루비 Marshal (쯔꾸르 XP/VX/VX Ace 세이브·데이터) ─────────
    class RNode
    {
        public List<KeyValuePair<string, RNode>> Ivars; // 'I' (문자열 인코딩 등)
        public List<string> Ext;                        // 'e'
        public string UClass;                           // 'C'
    }
    class RConst : RNode { public char T; }             // '0' nil, 'T', 'F'
    class RInt : RNode { public long V; }
    class RSym : RNode { public string Name; }
    class RFloat : RNode { public byte[] Raw; public double V; }
    class RBig : RNode { public byte Sign; public byte[] Data; }
    class RStr : RNode { public byte[] B; }
    class RRegex : RNode { public byte[] B; public byte Opt; }
    class RArr : RNode { public List<RNode> Items = new List<RNode>(); }
    class RHash : RNode { public List<RNode> Keys = new List<RNode>(), Vals = new List<RNode>(); public RNode Default; }
    class RObj : RNode { public string Cls; public List<string> Names = new List<string>(); public List<RNode> Vals = new List<RNode>(); }
    class RStruct : RObj { }
    class RUser : RNode { public string Cls; public byte[] Data; }   // 'u' (Table, Color, Tone ...)
    class RUMar : RNode { public string Cls; public RNode Data; }    // 'U'
    class RDataObj : RNode { public string Cls; public RNode Data; } // 'd'
    class RClass : RNode { public char T; public string Name; }      // 'c' 'm' 'M'

    class RefEq<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T a, T b) { return ReferenceEquals(a, b); }
        public int GetHashCode(T o) { return RuntimeHelpers.GetHashCode(o); }
    }

    static class RMarshal
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        // ---------- 읽기 ----------
        class Reader
        {
            readonly byte[] d; public int P;
            readonly List<string> syms = new List<string>();
            readonly List<RNode> objs = new List<RNode>();
            public readonly Dictionary<string, List<KeyValuePair<string, RNode>>> SymIvars;
            public Reader(byte[] d, int p, Dictionary<string, List<KeyValuePair<string, RNode>>> si) { this.d = d; P = p; SymIvars = si; }

            byte B() { if (P >= d.Length) throw new EndOfStreamException("데이터가 중간에 끊겼어요"); return d[P++]; }

            long Long()
            {
                sbyte c = (sbyte)B();
                if (c == 0) return 0;
                if (c > 0)
                {
                    if (c > 4) return c - 5;
                    long x = 0;
                    for (int i = 0; i < c; i++) x |= (long)B() << (8 * i);
                    return x;
                }
                if (c < -4) return c + 5;
                long y = -1;
                for (int i = 0; i < -c; i++) { y &= ~(0xffL << (8 * i)); y |= (long)B() << (8 * i); }
                return y;
            }

            byte[] Bytes() { int n = (int)Long(); var r = new byte[n]; Array.Copy(d, P, r, 0, n); P += n; return r; }

            string Sym()
            {
                byte t = B();
                bool iv = false;
                if (t == 'I') { iv = true; t = B(); }
                if (t == ';') return syms[(int)Long()];
                if (t != ':') throw new FormatException("심볼 형식이 아니에요 (" + (char)t + ")");
                string s = Utf8.GetString(Bytes());
                syms.Add(s);
                if (iv)
                {
                    int n = (int)Long();
                    var l = new List<KeyValuePair<string, RNode>>();
                    for (int i = 0; i < n; i++) { string k = Sym(); l.Add(new KeyValuePair<string, RNode>(k, Obj())); }
                    SymIvars[s] = l;
                }
                return s;
            }

            T Reg<T>(T n) where T : RNode { objs.Add(n); return n; }

            public RNode Obj()
            {
                byte t = B();
                switch ((char)t)
                {
                    case '0': return new RConst { T = '0' };
                    case 'T': return new RConst { T = 'T' };
                    case 'F': return new RConst { T = 'F' };
                    case 'i': return new RInt { V = Long() };
                    case ':': P--; return new RSym { Name = Sym() };
                    case ';': P--; return new RSym { Name = Sym() };
                    case '@': return objs[(int)Long()];
                    case 'I':
                        {
                            if (d[P] == ':') { P--; return new RSym { Name = Sym() }; }
                            var inner = Obj();
                            int n = (int)Long();
                            inner.Ivars = new List<KeyValuePair<string, RNode>>();
                            for (int i = 0; i < n; i++) { string k = Sym(); inner.Ivars.Add(new KeyValuePair<string, RNode>(k, Obj())); }
                            return inner;
                        }
                    case 'e': { string m = Sym(); var inner = Obj(); (inner.Ext ?? (inner.Ext = new List<string>())).Insert(0, m); return inner; }
                    case 'C': { string c = Sym(); var inner = Obj(); inner.UClass = c; return inner; }
                    case '"': return Reg(new RStr { B = Bytes() });
                    case 'f':
                        {
                            var raw = Bytes();
                            string s = Encoding.ASCII.GetString(raw);
                            int z = s.IndexOf('\0'); if (z >= 0) s = s.Substring(0, z);
                            double v = s == "inf" ? double.PositiveInfinity : s == "-inf" ? double.NegativeInfinity : s == "nan" ? double.NaN
                                : double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                            return Reg(new RFloat { Raw = raw, V = v });
                        }
                    case 'l': { byte sign = B(); int n = (int)Long(); var data = new byte[n * 2]; Array.Copy(d, P, data, 0, data.Length); P += data.Length; return Reg(new RBig { Sign = sign, Data = data }); }
                    case '/': { var r = Reg(new RRegex()); r.B = Bytes(); r.Opt = B(); return r; }
                    case '[':
                        {
                            var a = Reg(new RArr());
                            int n = (int)Long();
                            for (int i = 0; i < n; i++) a.Items.Add(Obj());
                            return a;
                        }
                    case '{':
                    case '}':
                        {
                            var h = Reg(new RHash());
                            int n = (int)Long();
                            for (int i = 0; i < n; i++) { h.Keys.Add(Obj()); h.Vals.Add(Obj()); }
                            if (t == '}') h.Default = Obj();
                            return h;
                        }
                    case 'o':
                    case 'S':
                        {
                            RObj o = t == 'S' ? new RStruct() : new RObj();
                            Reg(o);
                            o.Cls = Sym();
                            int n = (int)Long();
                            for (int i = 0; i < n; i++) { o.Names.Add(Sym()); o.Vals.Add(Obj()); }
                            return o;
                        }
                    case 'u': { string c = Sym(); var b = Bytes(); return Reg(new RUser { Cls = c, Data = b }); }
                    case 'U': { var u = Reg(new RUMar()); u.Cls = Sym(); u.Data = Obj(); return u; }
                    case 'd': { var x = Reg(new RDataObj()); x.Cls = Sym(); x.Data = Obj(); return x; }
                    case 'c':
                    case 'm':
                    case 'M': return Reg(new RClass { T = (char)t, Name = Utf8.GetString(Bytes()) });
                }
                throw new FormatException("알 수 없는 형식 '" + (char)t + "' @" + (P - 1));
            }
        }

        // 파일 하나에 Marshal.dump 가 여러 번 이어져 있음
        public static List<RNode> ReadAll(byte[] data, Dictionary<string, List<KeyValuePair<string, RNode>>> symIvars = null)
        {
            symIvars = symIvars ?? new Dictionary<string, List<KeyValuePair<string, RNode>>>();
            var list = new List<RNode>();
            int p = 0;
            while (p + 2 <= data.Length)
            {
                if (data[p] != 4 || data[p + 1] != 8) throw new FormatException("루비 데이터가 아니에요");
                var r = new Reader(data, p + 2, symIvars);
                list.Add(r.Obj());
                p = r.P;
            }
            return list;
        }

        // ---------- 쓰기 ----------
        class Writer
        {
            readonly MemoryStream o;
            readonly Dictionary<string, int> syms = new Dictionary<string, int>();
            readonly Dictionary<RNode, int> objs = new Dictionary<RNode, int>(new RefEq<RNode>());
            readonly Dictionary<string, List<KeyValuePair<string, RNode>>> symIvars;
            public Writer(MemoryStream o, Dictionary<string, List<KeyValuePair<string, RNode>>> si) { this.o = o; symIvars = si; }

            void B(int b) { o.WriteByte((byte)b); }

            void Long(long x)
            {
                if (x == 0) { B(0); return; }
                if (x > 0 && x < 123) { B((int)(x + 5)); return; }
                if (x < 0 && x > -124) { B((int)((x - 5) & 0xff)); return; }
                var buf = new byte[5];
                int i;
                for (i = 1; i < 5; i++)
                {
                    buf[i] = (byte)(x & 0xff);
                    x >>= 8;
                    if (x == 0) { buf[0] = (byte)i; break; }
                    if (x == -1) { buf[0] = (byte)(-i); break; }
                }
                o.Write(buf, 0, i + 1);
            }

            void Bytes(byte[] b) { Long(b.Length); o.Write(b, 0, b.Length); }

            void Sym(string s)
            {
                int idx;
                if (syms.TryGetValue(s, out idx)) { B(';'); Long(idx); return; }
                List<KeyValuePair<string, RNode>> iv = null;
                if (symIvars != null) symIvars.TryGetValue(s, out iv);
                if (iv != null) B('I');
                B(':');
                Bytes(Utf8.GetBytes(s));
                syms[s] = syms.Count;
                if (iv != null) { Long(iv.Count); foreach (var kv in iv) { Sym(kv.Key); Obj(kv.Value); } }
            }

            public void Obj(RNode n)
            {
                var c = n as RConst;
                if (c != null) { B(c.T); return; }
                var it = n as RInt;
                if (it != null)
                {
                    if (it.V >= -(1L << 30) && it.V < (1L << 30)) { B('i'); Long(it.V); return; }
                    // 큰 수는 Bignum 으로
                    var big = new RBig { Sign = (byte)(it.V < 0 ? '-' : '+') };
                    ulong u = (ulong)Math.Abs(it.V);
                    var bytes = new List<byte>();
                    while (u > 0) { bytes.Add((byte)(u & 0xff)); u >>= 8; }
                    if (bytes.Count % 2 == 1) bytes.Add(0);
                    big.Data = bytes.ToArray();
                    Obj(big);
                    return;
                }
                var sy = n as RSym;
                if (sy != null) { Sym(sy.Name); return; }

                int idx;
                if (objs.TryGetValue(n, out idx)) { B('@'); Long(idx); return; }
                bool iv = n.Ivars != null && n.Ivars.Count > 0;
                if (iv) B('I');
                if (n.Ext != null) foreach (var m in n.Ext) { B('e'); Sym(m); }
                if (n.UClass != null) { B('C'); Sym(n.UClass); }
                objs[n] = objs.Count;

                if (n is RStr) { B('"'); Bytes(((RStr)n).B); }
                else if (n is RFloat) { B('f'); Bytes(((RFloat)n).Raw); }
                else if (n is RBig) { var g = (RBig)n; B('l'); B(g.Sign); Long(g.Data.Length / 2); o.Write(g.Data, 0, g.Data.Length); }
                else if (n is RRegex) { var r = (RRegex)n; B('/'); Bytes(r.B); B(r.Opt); }
                else if (n is RArr) { var a = (RArr)n; B('['); Long(a.Items.Count); foreach (var x in a.Items) Obj(x); }
                else if (n is RHash)
                {
                    var h = (RHash)n;
                    B(h.Default != null ? '}' : '{');
                    Long(h.Keys.Count);
                    for (int i = 0; i < h.Keys.Count; i++) { Obj(h.Keys[i]); Obj(h.Vals[i]); }
                    if (h.Default != null) Obj(h.Default);
                }
                else if (n is RObj)
                {
                    var ob = (RObj)n;
                    B(n is RStruct ? 'S' : 'o');
                    Sym(ob.Cls);
                    Long(ob.Names.Count);
                    for (int i = 0; i < ob.Names.Count; i++) { Sym(ob.Names[i]); Obj(ob.Vals[i]); }
                }
                else if (n is RUser) { var u = (RUser)n; B('u'); Sym(u.Cls); Bytes(u.Data); }
                else if (n is RUMar) { var u = (RUMar)n; B('U'); Sym(u.Cls); Obj(u.Data); }
                else if (n is RDataObj) { var x = (RDataObj)n; B('d'); Sym(x.Cls); Obj(x.Data); }
                else if (n is RClass) { var k = (RClass)n; B(k.T); Bytes(Utf8.GetBytes(k.Name)); }
                else throw new InvalidOperationException("쓸 수 없는 노드");

                if (iv) { Long(n.Ivars.Count); foreach (var kv in n.Ivars) { Sym(kv.Key); Obj(kv.Value); } }
            }
        }

        public static byte[] WriteAll(IEnumerable<RNode> streams, Dictionary<string, List<KeyValuePair<string, RNode>>> symIvars)
        {
            using (var ms = new MemoryStream())
            {
                foreach (var s in streams)
                {
                    ms.WriteByte(4); ms.WriteByte(8);
                    new Writer(ms, symIvars).Obj(s);
                }
                return ms.ToArray();
            }
        }

        public static RFloat MakeFloat(double v)
        {
            string s = double.IsPositiveInfinity(v) ? "inf" : double.IsNegativeInfinity(v) ? "-inf" : double.IsNaN(v) ? "nan"
                : v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            return new RFloat { V = v, Raw = Encoding.ASCII.GetBytes(s) };
        }
    }

    // ───────── 루비 데이터 ↔ JSON 모양 (편집 화면을 MV 와 같이 쓰기 위함) ─────────
    // 객체 변수 @actor_id → _actorId (MV 와 같은 이름이 됨), 정수·심볼 키 해시 → 객체
    class RubyBridge
    {
        public const string ClassTag = "\u0001class:";
        readonly string prefix;
        readonly bool tables, ace;
        readonly Dictionary<RNode, JNode> toJ = new Dictionary<RNode, JNode>(new RefEq<RNode>());
        readonly Dictionary<JNode, RNode> orig = new Dictionary<JNode, RNode>(new RefEq<JNode>());
        readonly Dictionary<JNode, RNode> back = new Dictionary<JNode, RNode>(new RefEq<JNode>());
        readonly List<RNode> opaque = new List<RNode>();
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        public RubyBridge(bool underscore, bool tablesAsArrays, bool ace)
        {
            prefix = underscore ? "_" : "";
            tables = tablesAsArrays;
            this.ace = ace;
        }

        public static string Camel(string ivar)
        {
            var parts = ivar.TrimStart('@').Split('_');
            var sb = new StringBuilder(parts[0]);
            for (int i = 1; i < parts.Length; i++)
                if (parts[i].Length > 0) sb.Append(char.ToUpperInvariant(parts[i][0])).Append(parts[i].Substring(1));
            return sb.ToString();
        }

        public static string Snake(string camel)
        {
            var sb = new StringBuilder("@");
            foreach (char c in camel)
            {
                if (char.IsUpper(c)) sb.Append('_').Append(char.ToLowerInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        JNode Opaque(RNode n, string label)
        {
            var o = new JObj();
            o["@"] = JVal.S(label);
            o["@m"] = JVal.N(opaque.Count);
            opaque.Add(n);
            toJ[n] = o;
            return o;
        }

        public JNode ToJ(RNode n)
        {
            JNode done;
            if (n is RConst)
            {
                char t = ((RConst)n).T;
                return t == '0' ? new JVal { Raw = "null" } : JVal.B(t == 'T');
            }
            if (n is RInt) return JVal.N(((RInt)n).V);
            if (n is RSym) return Opaque(n, ":" + ((RSym)n).Name);
            if (toJ.TryGetValue(n, out done)) return done;

            if (n is RStr)
            {
                var v = JVal.S(Utf8.GetString(((RStr)n).B));
                toJ[n] = v; orig[v] = n;
                return v;
            }
            if (n is RFloat) { var v = JVal.N(((RFloat)n).V); toJ[n] = v; orig[v] = n; return v; }
            if (n is RBig)
            {
                var g = (RBig)n;
                double val = 0;
                for (int i = g.Data.Length - 1; i >= 0; i--) val = val * 256 + g.Data[i];
                var v = JVal.N(g.Sign == '-' ? -val : val);
                toJ[n] = v; orig[v] = n;
                return v;
            }
            if (n is RClass) { var v = JVal.S(ClassTag + ((RClass)n).Name); toJ[n] = v; orig[v] = n; return v; }
            if (n is RArr)
            {
                var a = new JArr();
                toJ[n] = a; orig[a] = n;
                foreach (var x in ((RArr)n).Items) a.Items.Add(ToJ(x));
                return a;
            }
            if (n is RHash)
            {
                var h = (RHash)n;
                if (!h.Keys.All(k => k is RInt || k is RSym)) return Opaque(n, "Hash");
                var o = new JObj();
                toJ[n] = o; orig[o] = n;
                for (int i = 0; i < h.Keys.Count; i++)
                {
                    string k = h.Keys[i] is RInt ? ((RInt)h.Keys[i]).V.ToString() : ((RSym)h.Keys[i]).Name;
                    o[k] = ToJ(h.Vals[i]);
                }
                return o;
            }
            if (n is RObj)
            {
                var ob = (RObj)n;
                var o = new JObj();
                toJ[n] = o; orig[o] = n;
                o["@"] = JVal.S(ob.Cls);
                for (int i = 0; i < ob.Names.Count; i++) o[prefix + Camel(ob.Names[i])] = ToJ(ob.Vals[i]);
                return o;
            }
            if (n is RUser && tables && ((RUser)n).Cls == "Table") return TableToJ(((RUser)n).Data);
            return Opaque(n, n is RUser ? ((RUser)n).Cls : n is RUMar ? ((RUMar)n).Cls : "루비 데이터");
        }

        // Table → [x][y] 2차원 배열 (데이터베이스 읽기 전용)
        static JNode TableToJ(byte[] b)
        {
            int xs = BitConverter.ToInt32(b, 4), ys = BitConverter.ToInt32(b, 8);
            var a = new JArr();
            for (int x = 0; x < xs; x++)
            {
                var row = new JArr();
                for (int y = 0; y < ys; y++)
                {
                    int i = 20 + (x + y * xs) * 2;
                    row.Items.Add(JVal.N(i + 1 < b.Length ? BitConverter.ToInt16(b, i) : 0));
                }
                a.Items.Add(row);
            }
            return a;
        }

        static double BigVal(RBig g)
        {
            double val = 0;
            for (int i = g.Data.Length - 1; i >= 0; i--) val = val * 256 + g.Data[i];
            return g.Sign == '-' ? -val : val;
        }

        public void BeginWrite() { back.Clear(); }

        public RNode FromJ(JNode j)
        {
            RNode r;
            if (back.TryGetValue(j, out r)) return r;
            RNode o;
            orig.TryGetValue(j, out o);

            var v = j as JVal;
            if (v != null)
            {
                if (v.IsStr)
                {
                    if (v.Str.StartsWith(ClassTag))
                    {
                        string name = v.Str.Substring(ClassTag.Length);
                        var oc = o as RClass;
                        r = oc != null && oc.Name == name ? (RNode)oc : new RClass { T = 'c', Name = name };
                    }
                    else if (o is RStr)
                    {
                        var rs = (RStr)o;
                        if (Utf8.GetString(rs.B) != v.Str) rs.B = Utf8.GetBytes(v.Str);
                        r = rs;
                    }
                    else
                    {
                        var ns = new RStr { B = Utf8.GetBytes(v.Str) };
                        if (ace) ns.Ivars = new List<KeyValuePair<string, RNode>> { new KeyValuePair<string, RNode>("E", new RConst { T = 'T' }) };
                        r = ns;
                    }
                }
                else if (v.Raw == "null") r = new RConst { T = '0' };
                else if (v.Raw == "true") r = new RConst { T = 'T' };
                else if (v.Raw == "false") r = new RConst { T = 'F' };
                else
                {
                    double d = v.Num;
                    if (o is RFloat) r = ((RFloat)o).V == d ? o : RMarshal.MakeFloat(d);
                    else if (o is RBig && BigVal((RBig)o) == d) r = o;
                    else if (d == Math.Floor(d) && Math.Abs(d) < 9e15) r = new RInt { V = (long)d };
                    else r = RMarshal.MakeFloat(d);
                }
                if (!(r is RInt) && !(r is RConst)) back[j] = r;
                return r;
            }

            var a = j as JArr;
            if (a != null)
            {
                var ra = o as RArr ?? new RArr();
                back[j] = ra;
                var items = a.Items.Select(FromJ).ToList();
                ra.Items = items;
                return ra;
            }

            var jo = (JObj)j;
            var m = jo["@m"] as JVal;
            if (m != null && m.IsNum) { r = opaque[(int)m.Num]; back[j] = r; return r; }

            if (o is RObj || (o == null && jo["@"] is JVal))
            {
                var ob = o as RObj ?? new RObj { Cls = Json.Str(jo, "@") };
                back[j] = ob;
                var oldNames = ob.Names.ToList();
                var names = new List<string>();
                var vals = new List<RNode>();
                foreach (var k in jo.Keys)
                {
                    if (k.StartsWith("@")) continue;
                    string bare = prefix.Length > 0 && k.StartsWith(prefix) ? k.Substring(prefix.Length) : k;
                    string name = oldNames.FirstOrDefault(nm => Camel(nm) == bare) ?? Snake(bare);
                    names.Add(name);
                    vals.Add(FromJ(jo.Map[k]));
                }
                ob.Names = names; ob.Vals = vals;
                return ob;
            }

            var rh = o as RHash ?? new RHash();
            back[j] = rh;
            var oldKeys = new Dictionary<string, RNode>();
            foreach (var k in rh.Keys) oldKeys[k is RInt ? ((RInt)k).V.ToString() : k is RSym ? ((RSym)k).Name : "?"] = k;
            var nk = new List<RNode>();
            var nv = new List<RNode>();
            foreach (var k in jo.Keys)
            {
                if (k.StartsWith("@")) continue;
                RNode key;
                long id;
                if (!oldKeys.TryGetValue(k, out key)) key = long.TryParse(k, out id) ? (RNode)new RInt { V = id } : new RSym { Name = k };
                nk.Add(key);
                nv.Add(FromJ(jo.Map[k]));
            }
            rh.Keys = nk; rh.Vals = nv;
            return rh;
        }
    }

    // ───────── 쯔꾸르 XP/VX/VX Ace 세이브 파일 ─────────
    class RubySave
    {
        static readonly string[] XpNames = { "characters", "frameCount", "system", "switches", "variables", "selfSwitches", "screen", "actors", "party", "troop", "map", "player" };
        static readonly string[] VxNames = { "characters", "frameCount", "lastBgm", "lastBgs", "system", "message", "switches", "variables", "selfSwitches", "actors", "party", "troop", "map", "player" };

        List<RNode> streams;
        Dictionary<string, List<KeyValuePair<string, RNode>>> symIvars = new Dictionary<string, List<KeyValuePair<string, RNode>>>();
        RubyBridge br;
        RpgKind kind;
        public JObj Root;

        string[] Names { get { return kind == RpgKind.XP ? XpNames : VxNames; } }

        public static RubySave Load(string path, RpgKind kind)
        {
            var s = new RubySave { kind = kind };
            s.streams = RMarshal.ReadAll(File.ReadAllBytes(path), s.symIvars);
            s.br = new RubyBridge(true, false, kind == RpgKind.VXAce);
            if (kind == RpgKind.VXAce)
            {
                if (s.streams.Count < 2) throw new Exception("VX Ace 세이브 모양이 아니에요");
                s.Root = s.br.ToJ(s.streams[1]) as JObj;
                if (s.Root == null) throw new Exception("VX Ace 세이브 내용을 읽지 못했어요");
            }
            else
            {
                s.Root = new JObj();
                for (int i = 0; i < s.streams.Count && i < s.Names.Length; i++) s.Root[s.Names[i]] = s.br.ToJ(s.streams[i]);
            }
            return s;
        }

        public byte[] Build()
        {
            br.BeginWrite();
            if (kind == RpgKind.VXAce) streams[1] = br.FromJ(Root);
            else for (int i = 0; i < streams.Count && i < Names.Length; i++) streams[i] = br.FromJ(Root[Names[i]]);
            return RMarshal.WriteAll(streams, symIvars);
        }

        public void Save(string path)
        {
            var bytes = Build();
            var check = RMarshal.ReadAll(bytes);
            if (check.Count != streams.Count) throw new Exception("저장 확인에 실패해서 원본을 건드리지 않았어요");
            string tmp = path + ".minicheat.tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }
    }

    // ───────── RGSS 암호 압축 파일 (Game.rgssad / .rgss2a / .rgss3a) ─────────
    class RgssArchive
    {
        class Entry { public long Off; public int Size; public uint Key; }
        readonly string path;
        readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static RgssArchive Open(string root)
        {
            foreach (var n in new[] { "Game.rgss3a", "Game.rgss2a", "Game.rgssad" })
            {
                string p = Path.Combine(root, n);
                if (File.Exists(p)) { try { return new RgssArchive(p); } catch { } }
            }
            return null;
        }

        static string Norm(string s) { return s.Replace('/', '\\').TrimStart('\\'); }

        RgssArchive(string p)
        {
            path = p;
            using (var fs = File.OpenRead(p))
            using (var r = new BinaryReader(fs))
            {
                var magic = r.ReadBytes(8);
                if (Encoding.ASCII.GetString(magic, 0, 6) != "RGSSAD") throw new FormatException();
                if (magic[7] == 3)
                {
                    uint key = r.ReadUInt32() * 9 + 3;
                    while (fs.Position < fs.Length)
                    {
                        uint off = r.ReadUInt32() ^ key;
                        if (off == 0) break;
                        uint size = r.ReadUInt32() ^ key, fkey = r.ReadUInt32() ^ key, nlen = r.ReadUInt32() ^ key;
                        var nb = r.ReadBytes((int)nlen);
                        for (int i = 0; i < nb.Length; i++) nb[i] ^= (byte)(key >> (8 * (i % 4)));
                        map[Norm(Encoding.UTF8.GetString(nb))] = new Entry { Off = off, Size = (int)size, Key = fkey };
                    }
                }
                else
                {
                    uint key = 0xDEADCAFE;
                    while (fs.Position + 4 <= fs.Length)
                    {
                        uint nlen = r.ReadUInt32() ^ key; key = key * 7 + 3;
                        var nb = r.ReadBytes((int)nlen);
                        for (int i = 0; i < nb.Length; i++) { nb[i] ^= (byte)(key & 0xff); key = key * 7 + 3; }
                        uint size = r.ReadUInt32() ^ key; key = key * 7 + 3;
                        map[Norm(Encoding.UTF8.GetString(nb))] = new Entry { Off = fs.Position, Size = (int)size, Key = key };
                        fs.Position += size;
                    }
                }
            }
        }

        public byte[] Get(string name)
        {
            Entry e;
            if (!map.TryGetValue(Norm(name), out e)) return null;
            var b = new byte[e.Size];
            using (var fs = File.OpenRead(path)) { fs.Position = e.Off; fs.Read(b, 0, b.Length); }
            uint k = e.Key;
            for (int i = 0; i < b.Length; i += 4)
            {
                for (int j = 0; j < 4 && i + j < b.Length; j++) b[i + j] ^= (byte)(k >> (8 * j));
                k = k * 7 + 3;
            }
            return b;
        }

        public bool Has(string name) { return map.ContainsKey(Norm(name)); }
    }
}
