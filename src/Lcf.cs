using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MiniCheat
{
    // ───────── 쯔꾸르 2000/2003 (LCF: RPG_RT.ldb / Save01.lsd) ─────────
    class LcfChunk { public int Id; public byte[] Data; }

    class LcfStruct
    {
        public List<LcfChunk> Chunks = new List<LcfChunk>();
        public bool Term;

        public byte[] Get(int id) { var c = Chunks.FirstOrDefault(x => x.Id == id); return c == null ? null : c.Data; }

        public void Set(int id, byte[] d)
        {
            var c = Chunks.FirstOrDefault(x => x.Id == id);
            if (c != null) { c.Data = d; return; }
            int i = Chunks.FindIndex(x => x.Id > id);
            var n = new LcfChunk { Id = id, Data = d };
            if (i < 0) Chunks.Add(n); else Chunks.Insert(i, n);
        }
    }

    static class Lcf
    {
        public static uint Ber(byte[] b, ref int p)
        {
            uint v = 0;
            while (true)
            {
                byte x = b[p++];
                v = (v << 7) | (uint)(x & 0x7f);
                if ((x & 0x80) == 0) return v;
            }
        }

        public static byte[] BerBytes(uint v)
        {
            var l = new List<byte> { (byte)(v & 0x7f) };
            v >>= 7;
            while (v > 0) { l.Insert(0, (byte)((v & 0x7f) | 0x80)); v >>= 7; }
            return l.ToArray();
        }

        public static LcfStruct Struct(byte[] b, ref int p, int end)
        {
            var s = new LcfStruct();
            while (p < end)
            {
                int id = (int)Ber(b, ref p);
                if (id == 0) { s.Term = true; break; }
                int size = (int)Ber(b, ref p);
                var d = new byte[size];
                System.Array.Copy(b, p, d, 0, size);
                p += size;
                s.Chunks.Add(new LcfChunk { Id = id, Data = d });
            }
            return s;
        }

        public static LcfStruct Struct(byte[] d) { int p = 0; return d == null ? new LcfStruct { Term = true } : Struct(d, ref p, d.Length); }

        public static void Write(MemoryStream o, LcfStruct s)
        {
            foreach (var c in s.Chunks)
            {
                var a = BerBytes((uint)c.Id); o.Write(a, 0, a.Length);
                var l = BerBytes((uint)c.Data.Length); o.Write(l, 0, l.Length);
                o.Write(c.Data, 0, c.Data.Length);
            }
            if (s.Term) o.WriteByte(0);
        }

        public static byte[] Bytes(LcfStruct s) { using (var o = new MemoryStream()) { Write(o, s); return o.ToArray(); } }

        public static List<KeyValuePair<int, LcfStruct>> Array(byte[] d)
        {
            var r = new List<KeyValuePair<int, LcfStruct>>();
            if (d == null || d.Length == 0) return r;
            int p = 0;
            int n = (int)Ber(d, ref p);
            for (int i = 0; i < n && p < d.Length; i++)
            {
                int idx = (int)Ber(d, ref p);
                r.Add(new KeyValuePair<int, LcfStruct>(idx, Struct(d, ref p, d.Length)));
            }
            return r;
        }

        public static byte[] ArrayBytes(List<KeyValuePair<int, LcfStruct>> list)
        {
            using (var o = new MemoryStream())
            {
                var c = BerBytes((uint)list.Count); o.Write(c, 0, c.Length);
                foreach (var kv in list)
                {
                    var i = BerBytes((uint)kv.Key); o.Write(i, 0, i.Length);
                    kv.Value.Term = true;
                    Write(o, kv.Value);
                }
                return o.ToArray();
            }
        }

        public static int Int(byte[] d, int def)
        {
            if (d == null || d.Length == 0) return def;
            int p = 0;
            return (int)Ber(d, ref p);
        }

        public static byte[] IntBytes(int v) { return BerBytes((uint)v); }

        public static short[] I16(byte[] d)
        {
            if (d == null) return new short[0];
            var r = new short[d.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = BitConverter.ToInt16(d, i * 2);
            return r;
        }

        public static byte[] I16Bytes(IEnumerable<int> v) { return v.SelectMany(x => BitConverter.GetBytes((short)x)).ToArray(); }

        public static int[] I32(byte[] d)
        {
            if (d == null) return new int[0];
            var r = new int[d.Length / 4];
            for (int i = 0; i < r.Length; i++) r[i] = BitConverter.ToInt32(d, i * 4);
            return r;
        }

        public static byte[] I32Bytes(IEnumerable<int> v) { return v.SelectMany(BitConverter.GetBytes).ToArray(); }

        // 파일 앞 "LcfSaveData" / "LcfDataBase" 머리말을 건너뜀
        public static int SkipHeader(byte[] b, string expect)
        {
            int p = 0;
            int n = (int)Ber(b, ref p);
            string h = Encoding.ASCII.GetString(b, p, Math.Min(n, b.Length - p));
            if (h != expect) throw new FormatException(expect + " 파일이 아니에요");
            return p + n;
        }

        // 일본어(932) / 한국어(949) / 중국어(936) 구분
        public static Encoding DetectEncoding(IEnumerable<byte[]> samples, string iniValue)
        {
            int forced;
            if (int.TryParse((iniValue ?? "").Trim(), out forced)) try { return Encoding.GetEncoding(forced); } catch { }
            int ko = 0, ja = 0, zh = 0;
            foreach (var s in samples)
            {
                if (s == null) continue;
                for (int i = 0; i < s.Length - 1; i++)
                {
                    byte c = s[i], d = s[i + 1];
                    if (c < 0x80) continue;
                    if (c >= 0xB0 && c <= 0xC8 && d >= 0xA1 && d <= 0xFE) ko++;
                    else if (c >= 0xC9 && c <= 0xF7 && d >= 0xA1 && d <= 0xFE) zh++;
                    else if (((c >= 0x81 && c <= 0x9F) || (c >= 0xE0 && c <= 0xEF)) && d >= 0x40 && d <= 0xFC) ja++;
                    i++;
                }
            }
            int cp = ja > ko + zh ? 932 : zh > ko ? 936 : 949;
            try { return Encoding.GetEncoding(cp); } catch { return Encoding.Default; }
        }

        // 2000/2003 그림: PNG / BMP / XYZ (팔레트 0번은 투명)
        public static Bitmap LoadImage(string pathNoExt)
        {
            try
            {
                foreach (var ext in new[] { ".png", ".bmp", ".xyz" })
                {
                    string p = pathNoExt + ext;
                    if (!File.Exists(p)) continue;
                    var b = File.ReadAllBytes(p);
                    if (ext == ".xyz") return Xyz(b);
                    Bitmap bmp;
                    using (var ms = new MemoryStream(b)) using (var t = new Bitmap(ms))
                    {
                        Color key = Color.Empty;
                        if ((t.PixelFormat & PixelFormat.Indexed) != 0 && t.Palette.Entries.Length > 0) key = t.Palette.Entries[0];
                        bmp = new Bitmap(t);
                        if (key != Color.Empty) bmp.MakeTransparent(key);
                    }
                    return bmp;
                }
            }
            catch { }
            return null;
        }

        static Bitmap Xyz(byte[] b)
        {
            if (b.Length < 10 || Encoding.ASCII.GetString(b, 0, 4) != "XYZ1") return null;
            int w = BitConverter.ToUInt16(b, 4), h = BitConverter.ToUInt16(b, 6);
            byte[] raw;
            using (var ms = new MemoryStream(b, 10, b.Length - 10))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var o = new MemoryStream()) { ds.CopyTo(o); raw = o.ToArray(); }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int idx = raw[768 + y * w + x];
                    if (idx == 0) continue;
                    bmp.SetPixel(x, y, Color.FromArgb(raw[idx * 3], raw[idx * 3 + 1], raw[idx * 3 + 2]));
                }
            return bmp;
        }

        // 2000/2003 은 아이템 아이콘이 없어서 종류별 작은 그림을 만들어 씀
        public static Image TypeIcon(int type, int size)
        {
            string[] letters = { "도", "검", "방", "갑", "투", "장", "약", "책", "재", "특", "S", "", "", "", "", "", "", "", "", "", "기" };
            Color[] colors = { Color.FromArgb(120, 130, 150), Color.FromArgb(200, 80, 60), Color.FromArgb(90, 120, 200), Color.FromArgb(70, 140, 110),
                Color.FromArgb(150, 110, 70), Color.FromArgb(200, 160, 40), Color.FromArgb(60, 170, 90), Color.FromArgb(140, 90, 170),
                Color.FromArgb(150, 150, 150), Color.FromArgb(210, 110, 160), Color.FromArgb(80, 160, 190) };
            var bmp = new Bitmap(size, size);
            if (type < 0) return bmp;
            Color col = type < colors.Length ? colors[type] : Color.FromArgb(70, 110, 190);
            string ch = type < letters.Length ? letters[type] : "";
            int m = Math.Max(2, size / 8), s = size - m * 2;
            using (var g = Graphics.FromImage(bmp))
            using (var br = new SolidBrush(col))
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int r = s / 3;
                path.AddArc(m, m, r, r, 180, 90); path.AddArc(m + s - r, m, r, r, 270, 90);
                path.AddArc(m + s - r, m + s - r, r, r, 0, 90); path.AddArc(m, m + s - r, r, r, 90, 90);
                path.CloseFigure();
                g.FillPath(br, path);
                using (var f = new Font("맑은 고딕", Math.Max(6f, s * 0.42f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(ch, f, Brushes.White, new RectangleF(m, m, s, s), sf);
            }
            return bmp;
        }
    }

    // ───────── 2000/2003 세이브 (Save01.lsd) ↔ 편집 화면용 모양 ─────────
    class LsdSave
    {
        byte[] header;
        LcfStruct top, sys, inv;
        List<KeyValuePair<int, LcfStruct>> actors;
        Encoding enc;
        GameData db;
        public JObj Root;
        // 처음 읽은 값 (바뀐 것만 쓰기 위해)
        readonly Dictionary<string, string> orig = new Dictionary<string, string>();

        static readonly string[] ModKeys = { "_maxhpPlus", "_maxmpPlus", "_atkPlus", "_defPlus", "_spiPlus", "_agiPlus" };
        static readonly int[] ModIds = { 0x21, 0x22, 0x29, 0x2A, 0x2B, 0x2C };
        // 장비: 세이브 순서 = 무기, 방패, 갑옷, 투구, 장신구 → 화면 칸(무기·방패·머리·몸·장신구)
        static readonly string[] EquipKeys = { "_weaponId", "_armor1Id", "_armor3Id", "_armor2Id", "_armor4Id" };

        string S(byte[] d) { return d == null ? null : enc.GetString(d); }

        public static LsdSave Load(string path, GameData db)
        {
            var s = new LsdSave { db = db, enc = db.Enc ?? Encoding.Default };
            var b = File.ReadAllBytes(path);
            int p = Lcf.SkipHeader(b, "LcfSaveData");
            s.header = new byte[p];
            System.Array.Copy(b, s.header, p);
            s.top = Lcf.Struct(b, ref p, b.Length);
            s.sys = Lcf.Struct(s.top.Get(0x65));
            s.inv = Lcf.Struct(s.top.Get(0x6D));
            s.actors = Lcf.Array(s.top.Get(0x6C));
            s.Build();
            return s;
        }

        void Build()
        {
            Root = new JObj();
            Root["frameCount"] = JVal.N(Lcf.Int(sys.Get(0x0B), 0));

            var sw = new JArr(); sw.Items.Add(new JVal { Raw = "null" });
            var swb = sys.Get(0x20) ?? new byte[0];
            foreach (var x in swb) sw.Items.Add(JVal.B(x != 0));
            var swo = new JObj(); swo["_data"] = sw; Root["switches"] = swo;

            var va = new JArr(); va.Items.Add(new JVal { Raw = "null" });
            foreach (var x in Lcf.I32(sys.Get(0x22))) va.Items.Add(JVal.N(x));
            var vao = new JObj(); vao["_data"] = va; Root["variables"] = vao;
            orig["sw"] = Json.Write(sw); orig["var"] = Json.Write(va);

            var party = new JObj();
            party["_gold"] = JVal.N(Lcf.Int(inv.Get(0x15), 0));
            party["_steps"] = JVal.N(Lcf.Int(inv.Get(0x2A), 0));
            var pa = new JArr();
            foreach (var x in Lcf.I16(inv.Get(0x02))) pa.Items.Add(JVal.N(x));
            party["_actors"] = pa;
            var items = new JObj();
            var ids = Lcf.I16(inv.Get(0x0C));
            var counts = inv.Get(0x0D) ?? new byte[0];
            for (int i = 0; i < ids.Length; i++) items[ids[i].ToString()] = JVal.N(i < counts.Length ? counts[i] : 1);
            party["_items"] = items;
            Root["party"] = party;
            orig["items"] = Json.Write(items);

            var data = new JArr(); data.Items.Add(new JVal { Raw = "null" });
            foreach (var kv in actors)
            {
                var st = kv.Value;
                int id = kv.Key;
                var a = new JObj();
                a["@"] = JVal.S("Game_Actor");
                a["_actorId"] = JVal.N(id);
                JObj dba;
                db.Actors.TryGetValue(id, out dba);
                string name = S(st.Get(0x01));
                if (string.IsNullOrEmpty(name) || name == "\u0001") name = Json.Str(dba, "name");
                a["_name"] = JVal.S(name);
                int lv = Lcf.Int(st.Get(0x1F), -1);
                if (lv <= 0) lv = (int)Json.Num(dba, "initialLevel", 1);
                a["_level"] = JVal.N(lv);
                a["_exp"] = JVal.N(Math.Max(0, Lcf.Int(st.Get(0x20), 0)));
                a["_hp"] = JVal.N(Math.Max(0, Lcf.Int(st.Get(0x47), 0)));
                a["_mp"] = JVal.N(Math.Max(0, Lcf.Int(st.Get(0x48), 0)));
                for (int i = 0; i < 6; i++) a[ModKeys[i]] = JVal.N(Math.Max(0, Lcf.Int(st.Get(ModIds[i]), 0)));
                int cls = Lcf.Int(st.Get(0x5A), -1);
                a["_classId"] = JVal.N(cls >= 0 ? cls : (int)Json.Num(dba, "classId", 0));
                string face = S(st.Get(0x15));
                a["_faceName"] = JVal.S(string.IsNullOrEmpty(face) ? Json.Str(dba, "faceName") : face);
                a["_faceIndex"] = JVal.N(string.IsNullOrEmpty(face) ? Json.Num(dba, "faceIndex") : Lcf.Int(st.Get(0x16), 0));
                var sk = new JArr();
                foreach (var x in Lcf.I16(st.Get(0x34))) sk.Items.Add(JVal.N(x));
                a["_skills"] = sk;
                var eq = Lcf.I16(st.Get(0x3D));
                for (int i = 0; i < 5; i++) a[EquipKeys[i]] = JVal.N(i < eq.Length ? eq[i] : 0);
                data.Items.Add(a);
                foreach (var k in a.Keys) orig[id + k] = Json.Write(a.Map[k]);
            }
            var ad = new JObj(); ad["_data"] = data; Root["actors"] = ad;
        }

        bool Changed(int id, JObj a, string k) { string o; return !orig.TryGetValue(id + k, out o) || o != Json.Write(a[k] ?? new JVal { Raw = "null" }); }

        public byte[] Bytes()
        {
            sys.Set(0x0B, Lcf.IntBytes((int)Json.Num(Root, "frameCount")));

            var sw = Json.Arr(Json.Get(Root, "switches"), "_data");
            if (Json.Write(sw) != orig["sw"])
            {
                var swb = sw.Items.Skip(1).Select(n => (byte)(Json.Bool(n) ? 1 : 0)).ToArray();
                sys.Set(0x1F, Lcf.IntBytes(swb.Length)); sys.Set(0x20, swb);
            }
            var va = Json.Arr(Json.Get(Root, "variables"), "_data");
            if (Json.Write(va) != orig["var"])
            {
                var vals = va.Items.Skip(1).Select(n => (int)Json.Num(n)).ToList();
                sys.Set(0x21, Lcf.IntBytes(vals.Count)); sys.Set(0x22, Lcf.I32Bytes(vals));
            }
            top.Set(0x65, Lcf.Bytes(sys));

            var party = Json.Obj(Root, "party");
            inv.Set(0x15, Lcf.IntBytes((int)Json.Num(party, "_gold")));
            inv.Set(0x2A, Lcf.IntBytes((int)Json.Num(party, "_steps")));
            var items = Json.Obj(party, "_items");
            if (Json.Write(items) != orig["items"])
            {
                // 원래 순서 유지, 새 아이템은 뒤에. 사용 횟수(usage)는 그대로
                var oldIds = Lcf.I16(inv.Get(0x0C)).ToList();
                var oldUse = inv.Get(0x0E) ?? new byte[0];
                var ids = new List<int>(); var cnt = new List<byte>(); var use = new List<byte>();
                foreach (var k in items.Keys)
                {
                    int id, n = (int)Json.Num(items[k]);
                    if (!int.TryParse(k, out id) || n <= 0) continue;
                    ids.Add(id); cnt.Add((byte)Math.Min(255, n));
                    int oi = oldIds.IndexOf((short)id);
                    use.Add(oi >= 0 && oi < oldUse.Length ? oldUse[oi] : (byte)0);
                }
                inv.Set(0x0B, Lcf.IntBytes(ids.Count)); inv.Set(0x0C, Lcf.I16Bytes(ids));
                inv.Set(0x0D, cnt.ToArray()); inv.Set(0x0E, use.ToArray());
            }
            top.Set(0x6D, Lcf.Bytes(inv));

            var data = Json.Arr(Json.Get(Root, "actors"), "_data");
            JObj leader = null;
            int leaderId = (int)Json.Num(Json.At(Json.Arr(party, "_actors"), 0), -1);
            foreach (var kv in actors)
            {
                int id = kv.Key;
                var st = kv.Value;
                var a = data.Items.OfType<JObj>().FirstOrDefault(x => (int)Json.Num(x, "_actorId") == id);
                if (a == null) continue;
                if (id == leaderId) leader = a;
                if (Changed(id, a, "_name")) st.Set(0x01, enc.GetBytes(Json.Str(a, "_name")));
                if (Changed(id, a, "_level")) st.Set(0x1F, Lcf.IntBytes((int)Json.Num(a, "_level")));
                if (Changed(id, a, "_exp")) st.Set(0x20, Lcf.IntBytes((int)Json.Num(a, "_exp")));
                if (Changed(id, a, "_hp")) st.Set(0x47, Lcf.IntBytes((int)Json.Num(a, "_hp")));
                if (Changed(id, a, "_mp")) st.Set(0x48, Lcf.IntBytes((int)Json.Num(a, "_mp")));
                for (int i = 0; i < 6; i++) if (Changed(id, a, ModKeys[i])) st.Set(ModIds[i], Lcf.IntBytes((int)Json.Num(a, ModKeys[i])));
                if (Changed(id, a, "_skills"))
                {
                    var sk = Json.Arr(a, "_skills").Items.Select(n => (int)Json.Num(n)).ToList();
                    st.Set(0x33, Lcf.IntBytes(sk.Count)); st.Set(0x34, Lcf.I16Bytes(sk));
                }
                if (EquipKeys.Any(k => Changed(id, a, k)))
                    st.Set(0x3D, Lcf.I16Bytes(EquipKeys.Select(k => (int)Json.Num(a, k))));
            }
            top.Set(0x6C, Lcf.ArrayBytes(actors));

            // 불러오기 화면에 보이는 대표 캐릭터 레벨·HP 도 맞춤
            if (leader != null && (Changed(leaderId, leader, "_level") || Changed(leaderId, leader, "_hp")))
            {
                var title = Lcf.Struct(top.Get(0x64));
                title.Set(0x0C, Lcf.IntBytes((int)Json.Num(leader, "_level")));
                title.Set(0x0D, Lcf.IntBytes((int)Json.Num(leader, "_hp")));
                top.Set(0x64, Lcf.Bytes(title));
            }

            using (var o = new MemoryStream())
            {
                o.Write(header, 0, header.Length);
                Lcf.Write(o, top);
                return o.ToArray();
            }
        }

        public void Save(string path)
        {
            var bytes = Bytes();
            int p = Lcf.SkipHeader(bytes, "LcfSaveData");
            var check = Lcf.Struct(bytes, ref p, bytes.Length);
            if (check.Get(0x6D) == null) throw new Exception("저장 확인에 실패해서 원본을 건드리지 않았어요");
            string tmp = path + ".minicheat.tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }
    }
}
