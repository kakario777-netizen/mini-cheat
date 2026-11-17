using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MiniCheat
{
    // 순서·형식을 그대로 보존하는 작은 JSON (세이브를 손상 없이 다시 쓰기 위함)
    abstract class JNode { }

    class JObj : JNode
    {
        public readonly List<string> Keys = new List<string>();
        public readonly Dictionary<string, JNode> Map = new Dictionary<string, JNode>();

        public JNode this[string k]
        {
            get { JNode v; return Map.TryGetValue(k, out v) ? v : null; }
            set { if (!Map.ContainsKey(k)) Keys.Add(k); Map[k] = value; }
        }

        public void Remove(string k) { if (Map.Remove(k)) Keys.Remove(k); }
    }

    class JArr : JNode
    {
        public readonly List<JNode> Items = new List<JNode>();
    }

    class JVal : JNode
    {
        public string Raw;   // 숫자 / true / false / null 원문
        public string Str;   // 문자열일 때 값
        public bool IsStr;

        public static JVal S(string s) { return new JVal { IsStr = true, Str = s }; }
        public static JVal N(double d) { return new JVal { Raw = Fmt(d) }; }
        public static JVal B(bool b) { return new JVal { Raw = b ? "true" : "false" }; }

        public static string Fmt(double d)
        {
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        public bool IsNum { get { return !IsStr && Raw != "true" && Raw != "false" && Raw != "null"; } }
        public bool IsBool { get { return !IsStr && (Raw == "true" || Raw == "false"); } }

        public double Num
        {
            get
            {
                double d;
                return IsNum && double.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
            }
        }
    }

    static class Json
    {
        // ---------- 편의 함수 ----------
        public static JNode Get(JNode n, string k) { var o = n as JObj; return o == null ? null : o[k]; }

        public static JObj Obj(JNode n, string k) { return Get(n, k) as JObj; }

        // MV 는 배열을 {"@c":1,"@a":[...]} 로 감싸 저장함
        public static JArr Arr(JNode n)
        {
            var a = n as JArr;
            if (a != null) return a;
            return Get(n, "@a") as JArr;
        }

        public static JArr Arr(JNode n, string k) { return Arr(Get(n, k)); }

        public static double Num(JNode n, double def = 0)
        {
            var v = n as JVal;
            return v != null && v.IsNum ? v.Num : def;
        }

        public static double Num(JNode n, string k, double def = 0) { return Num(Get(n, k), def); }

        public static string Str(JNode n, string k, string def = "")
        {
            var v = Get(n, k) as JVal;
            return v != null && v.IsStr ? v.Str : def;
        }

        public static bool Bool(JNode n)
        {
            var v = n as JVal;
            return v != null && v.Raw == "true";
        }

        public static void SetNum(JNode n, string k, double d) { var o = n as JObj; if (o != null) o[k] = JVal.N(d); }

        public static void SetAt(JArr a, int i, JNode v)
        {
            while (a.Items.Count <= i) a.Items.Add(new JVal { Raw = "null" });
            a.Items[i] = v;
        }

        public static JNode At(JArr a, int i) { return a != null && i >= 0 && i < a.Items.Count ? a.Items[i] : null; }

        // ---------- 읽기 ----------
        public static JNode Parse(string s)
        {
            int i = 0;
            var r = ParseVal(s, ref i);
            return r;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static JNode ParseVal(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 끝");
            char c = s[i];
            if (c == '{')
            {
                var o = new JObj();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = ParseStr(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("':' 필요 @" + i);
                    i++;
                    o[k] = ParseVal(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw new FormatException("',' 또는 '}' 필요 @" + i);
                }
            }
            if (c == '[')
            {
                var a = new JArr();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Items.Add(ParseVal(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new FormatException("',' 또는 ']' 필요 @" + i);
                }
            }
            if (c == '"') return JVal.S(ParseStr(s, ref i));
            int st = i;
            while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
            return new JVal { Raw = s.Substring(st, i - st) };
        }

        static string ParseStr(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("'\"' 필요 @" + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        // ---------- 쓰기 (JSON.stringify 와 같은 모양) ----------
        public static string Write(JNode n)
        {
            var sb = new StringBuilder(1 << 16);
            W(sb, n);
            return sb.ToString();
        }

        static void W(StringBuilder sb, JNode n)
        {
            var o = n as JObj;
            if (o != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var k in o.Keys)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WStr(sb, k); sb.Append(':'); W(sb, o.Map[k]);
                }
                sb.Append('}');
                return;
            }
            var a = n as JArr;
            if (a != null)
            {
                sb.Append('[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    W(sb, a.Items[i]);
                }
                sb.Append(']');
                return;
            }
            var v = (JVal)n;
            if (v.IsStr) WStr(sb, v.Str); else sb.Append(v.Raw);
        }

        static void WStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
