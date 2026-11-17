using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniCheat
{
    enum RpgKind { Unknown, R2000, XP, VX, VXAce, MV, MZ }

    class DbItem
    {
        public int Id;
        public string Name = "", Desc = "", RawName = "", RawDesc = "";
        public int Icon;
        public int IType = 1;  // 아이템: 1 보통, 2 중요
        public int EType;      // 방어구 장비 종류 (무기는 1)
        public int Kind;       // 0 아이템, 1 무기, 2 방어구, 3 스킬
        public override string ToString() { return Name; }
    }

    class SaveSlot
    {
        public string Path, Label, PlayTime, When;
        public int Slot;
    }

    // 게임 폴더에서 이름·아이콘·얼굴 그림 등 데이터베이스를 읽음
    class GameData
    {
        public string Root, DataDir, ImgDir, SaveDir, Title, BackupName, TitleRaw;
        public bool Ko;
        public string[] swRaw = new string[0], varRaw = new string[0];
        string[] paramRaw = new string[0], equipRaw = new string[0];
        readonly Dictionary<string, string> gameTr = new Dictionary<string, string>();   // 게임에 들어 있는 번역 (원문 → 한글)
        readonly Dictionary<string, string> locale = new Dictionary<string, string>();   // 번역 키 → 한글
        public int GameTrCount { get { return gameTr.Count; } }
        public RpgKind Kind;
        public string Currency = "G";
        public readonly Dictionary<int, DbItem> Items = new Dictionary<int, DbItem>(), Weapons = new Dictionary<int, DbItem>(),
            Armors = new Dictionary<int, DbItem>(), Skills = new Dictionary<int, DbItem>();
        public readonly Dictionary<int, JObj> Actors = new Dictionary<int, JObj>(), Classes = new Dictionary<int, JObj>();
        public string[] SwitchNames = new string[0], VarNames = new string[0];
        public string[] ParamNames = { "최대 HP", "최대 MP", "공격력", "방어력", "마법력", "마법 방어", "민첩성", "운" };
        public string[] EquipTypes = { "", "무기", "방패", "머리", "몸", "장신구" };

        byte[] key;
        Bitmap iconSet;
        bool iconTried;
        readonly Dictionary<string, Bitmap> faceSheets = new Dictionary<string, Bitmap>();

        public bool IsJs { get { return Kind == RpgKind.MV || Kind == RpgKind.MZ; } }
        public bool IsRuby { get { return Kind == RpgKind.XP || Kind == RpgKind.VX || Kind == RpgKind.VXAce; } }
        public bool CanEdit { get { return IsJs || IsRuby; } }
        RgssArchive arc;
        string rext;
        readonly List<string> xpIcons = new List<string>();
        public int ParamCount { get { return Kind == RpgKind.XP || Kind == RpgKind.VX ? 6 : 8; } }
        // VX/XP 는 능력치 추가값이 배열이 아니라 따로따로 저장됨
        public string[] PlusKeys
        {
            get
            {
                if (Kind == RpgKind.VX) return new[] { "_maxhpPlus", "_maxmpPlus", "_atkPlus", "_defPlus", "_spiPlus", "_agiPlus" };
                if (Kind == RpgKind.XP) return new[] { "_maxhpPlus", "_maxspPlus", "_strPlus", "_dexPlus", "_agiPlus", "_intPlus" };
                return null;
            }
        }
        public string MpKey { get { return Kind == RpgKind.XP ? "_sp" : "_mp"; } }

        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case RpgKind.R2000: return "쯔꾸르 2000/2003";
                    case RpgKind.XP: return "쯔꾸르 XP";
                    case RpgKind.VX: return "쯔꾸르 VX";
                    case RpgKind.VXAce: return "쯔꾸르 VX Ace";
                    case RpgKind.MV: return "쯔꾸르 MV";
                    case RpgKind.MZ: return "쯔꾸르 MZ";
                }
                return "알 수 없는 게임";
            }
        }

        // ---------- 게임 종류·폴더 찾기 ----------
        public static RpgKind DetectKind(string root)
        {
            string tmp;
            int k = RpgDetect.Detect(Path.Combine(root, "Game.exe"), out tmp);
            if (k == RpgDetect.K2000) return RpgKind.R2000;
            if (tmp == "쯔꾸르 XP") return RpgKind.XP;
            if (tmp == "쯔꾸르 VX") return RpgKind.VX;
            if (tmp == "쯔꾸르 VX Ace") return RpgKind.VXAce;
            if (tmp == "쯔꾸르 MV") return RpgKind.MV;
            if (tmp == "쯔꾸르 MZ") return RpgKind.MZ;
            return RpgKind.Unknown;
        }

        public static GameData Load(string root)
        {
            var g = new GameData { Root = root, Title = System.IO.Path.GetFileName(root.TrimEnd('\\', '/')) };
            g.TitleRaw = g.Title;
            g.Kind = DetectKind(root);
            if (g.IsJs)
            {
                string www = Path.Combine(root, "www");
                string baseDir = Directory.Exists(Path.Combine(www, "data")) ? www : root;
                g.DataDir = Path.Combine(baseDir, "data");
                g.ImgDir = Path.Combine(baseDir, "img");
                g.SaveDir = Directory.Exists(Path.Combine(www, "save")) || g.Kind == RpgKind.MV && baseDir == www
                    ? Path.Combine(www, "save") : Path.Combine(root, "save");
                try { g.LoadDb(); } catch { }
            }
            else if (g.IsRuby)
            {
                g.SaveDir = root;
                g.DataDir = Path.Combine(root, "Data");
                g.ImgDir = Path.Combine(root, "Graphics");
                g.rext = g.Kind == RpgKind.XP ? ".rxdata" : g.Kind == RpgKind.VX ? ".rvdata" : ".rvdata2";
                g.arc = RgssArchive.Open(root);
                try { g.LoadDb(); } catch { }
                if (g.Kind == RpgKind.XP) try
                    {
                        string ini = Path.Combine(root, "Game.ini");
                        if (File.Exists(ini))
                            foreach (var l in File.ReadAllLines(ini, Encoding.UTF8))
                                if (l.StartsWith("Title=", StringComparison.OrdinalIgnoreCase) && l.Length > 6) { g.TitleRaw = l.Substring(6).Trim(); g.Title = Clean(g.TitleRaw); }
                    }
                    catch { }
            }
            else g.SaveDir = root;
            g.BackupName = g.Title; // 번역과 상관없이 백업 폴더 이름은 고정
            return g;
        }

        JNode ReadData(string name)
        {
            if (IsJs)
            {
                string p = Path.Combine(DataDir, name + ".json");
                if (!File.Exists(p)) return null;
                return Json.Parse(File.ReadAllText(p, Encoding.UTF8));
            }
            var b = ReadGameFile("Data\\" + name + rext);
            if (b == null) return null;
            return new RubyBridge(false, true, Kind == RpgKind.VXAce).ToJ(RMarshal.ReadAll(b)[0]);
        }

        // 폴더에 있으면 폴더에서, 없으면 암호 압축 파일 안에서
        byte[] ReadGameFile(string rel)
        {
            string p = Path.Combine(Root, rel.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return File.ReadAllBytes(p);
            return arc == null ? null : arc.Get(rel);
        }

        public static string Clean(string s)
        {
            // \C[2] 같은 색 코드 제거
            return Regex.Replace(s ?? "", @"\\[A-Za-z]+\[[^\]]*\]|\\[{}.|!><^]", "").Trim();
        }

        void LoadList(string file, Dictionary<int, DbItem> dst, int kind)
        {
            var arr = ReadData(file) as JArr;
            if (arr == null) return;
            foreach (var n in arr.Items)
            {
                var o = n as JObj;
                if (o == null) continue;
                var it = new DbItem
                {
                    Id = (int)Json.Num(o, "id"),
                    RawName = Json.Str(o, "name"),
                    RawDesc = Json.Str(o, "description"),
                    Icon = (int)Json.Num(o, "iconIndex"),
                    IType = (int)Json.Num(o, "itypeId", 1),
                    EType = kind == 1 ? 1 : (int)Json.Num(o, "etypeId"),
                    Kind = kind
                };
                if (kind == 2 && Kind == RpgKind.VXAce) it.EType = (int)Json.Num(o, "etypeId") + 1;          // Ace 는 0부터
                if (kind == 2 && (Kind == RpgKind.VX || Kind == RpgKind.XP)) it.EType = (int)Json.Num(o, "kind") + 2;
                if (Kind == RpgKind.XP)
                {
                    string icn = Json.Str(o, "iconName");
                    int ix = xpIcons.IndexOf(icn);
                    if (icn.Length > 0 && ix < 0) { xpIcons.Add(icn); ix = xpIcons.Count - 1; }
                    it.Icon = icn.Length > 0 ? ix + 1 : 0;
                }
                it.Name = Clean(it.RawName); it.Desc = Clean(it.RawDesc);
                if (it.Name.Length == 0) continue; // 빈 칸(이름 없는 데이터)은 뺌
                dst[it.Id] = it;
            }
        }

        void LoadDb()
        {
            LoadList("Items", Items, 0);
            LoadList("Weapons", Weapons, 1);
            LoadList("Armors", Armors, 2);
            LoadList("Skills", Skills, 3);
            foreach (var pair in new[] { new { F = "Actors", D = Actors }, new { F = "Classes", D = Classes } })
            {
                var arr = ReadData(pair.F) as JArr;
                if (arr == null) continue;
                foreach (var n in arr.Items) { var o = n as JObj; if (o != null) pair.D[(int)Json.Num(o, "id")] = o; }
            }
            var sys = ReadData("System") as JObj;
            if (sys == null) return;
            string t = Json.Str(sys, "gameTitle");
            if (t.Length > 0) { Title = Clean(t); TitleRaw = t; }
            string cu = Json.Str(sys, "currencyUnit");
            if (cu.Length > 0) Currency = cu;
            swRaw = RawNames(Json.Arr(sys, "switches"));
            varRaw = RawNames(Json.Arr(sys, "variables"));
            equipRaw = RawNames(Json.Arr(sys, "equipTypes"));
            paramRaw = RawNames(Json.Arr(Json.Get(sys, "terms"), "params"));
            if (Kind == RpgKind.VXAce)
            {
                var et = RawNames(Json.Arr(Json.Get(sys, "terms"), "etypes"));
                equipRaw = new[] { "" }.Concat(et).ToArray();
            }
            else if (Kind == RpgKind.VX || Kind == RpgKind.XP)
            {
                var w = Json.Get(sys, Kind == RpgKind.VX ? "terms" : "words");
                paramRaw = (Kind == RpgKind.VX ? new[] { "hp", "mp", "atk", "def", "spi", "agi" } : new[] { "hp", "sp", "str", "dex", "agi", "int" })
                    .Select(nm => Json.Str(w, nm)).ToArray();
                equipRaw = new[] { "", Json.Str(w, "weapon"), Json.Str(w, "armor1"), Json.Str(w, "armor2"), Json.Str(w, "armor3"), Json.Str(w, "armor4") };
                string gold = Json.Str(w, "gold");
                if (gold.Length > 0) Currency = gold;
            }
            ApplyLang(false);
            string k = Json.Str(sys, "encryptionKey");
            if (k.Length == 32)
            {
                key = new byte[16];
                for (int i = 0; i < 16; i++) key[i] = Convert.ToByte(k.Substring(i * 2, 2), 16);
            }
        }

        static string[] RawNames(JArr a)
        {
            if (a == null) return new string[0];
            return a.Items.Select(n => { var v = n as JVal; return v != null && v.IsStr ? v.Str : ""; }).ToArray();
        }

        // ---------- 번역 ----------
        static readonly System.Text.RegularExpressions.Regex Ph = new System.Text.RegularExpressions.Regex(
            @"\$\{([^}]+)\}|#\{([^}]+)\}|\{\{([^}]+)\}\}|\\loc\[([^\]]+)\]|\\L\[([^\]]+)\]");

        static bool HasHangul(string s) { foreach (char c in s ?? "") if (c >= 0xAC00 && c <= 0xD7A3) return true; return false; }

        string Resolve(string raw)
        {
            string r;
            if (gameTr.TryGetValue(raw, out r)) return r;
            if (locale.TryGetValue(raw, out r)) return r;
            if (locale.Count > 0 && Ph.IsMatch(raw))
                return Ph.Replace(raw, m =>
                {
                    string key = m.Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1).First(g => g.Success).Value;
                    string v;
                    return locale.TryGetValue(key, out v) ? v : m.Value;
                });
            return raw;
        }

        // 화면에 보일 글자: 게임 내장 번역 → 자동 번역 사전 → 원문
        public string T(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (!Ko) return Clean(raw);
            string s = Clean(Resolve(raw));
            return Clean(Translator.Get(s) ?? s);
        }

        IEnumerable<string> AllRaw()
        {
            foreach (var d in new[] { Items, Weapons, Armors, Skills })
                foreach (var it in d.Values) { yield return it.RawName; yield return it.RawDesc; }
            foreach (var a in Actors.Values) { yield return Json.Str(a, "name"); yield return Json.Str(a, "nickname"); }
            foreach (var c in Classes.Values) yield return Json.Str(c, "name");
            foreach (var arr in new[] { swRaw, varRaw, equipRaw, paramRaw }) foreach (var x in arr) yield return x;
            if (TitleRaw != null) yield return TitleRaw;
        }

        // 게임 내장 번역을 거친 뒤에도 일본어·중국어로 남는 것 (자동 번역 대상)
        public IEnumerable<string> TextsForAuto()
        {
            return AllRaw().Where(r => !string.IsNullOrEmpty(r)).Select(r => Clean(Resolve(r))).Where(Translator.Needs).Distinct().ToList();
        }

        public void ApplyLang(bool ko)
        {
            Ko = ko;
            foreach (var d in new[] { Items, Weapons, Armors, Skills })
                foreach (var it in d.Values) { it.Name = T(it.RawName); it.Desc = T(it.RawDesc); }
            SwitchNames = swRaw.Select(T).ToArray();
            VarNames = varRaw.Select(T).ToArray();
            if (equipRaw.Length > 1) EquipTypes = equipRaw.Select(T).ToArray();
            string[] defP = Kind == RpgKind.VX ? new[] { "최대 HP", "최대 MP", "공격력", "방어력", "정신력", "민첩성", "", "" }
                : Kind == RpgKind.XP ? new[] { "최대 HP", "최대 SP", "힘", "기교", "민첩", "마력", "", "" }
                : new[] { "최대 HP", "최대 MP", "공격력", "방어력", "마법력", "마법 방어", "민첩성", "운" };
            for (int i = 0; i < 8; i++) ParamNames[i] = i < paramRaw.Length && paramRaw[i].Length > 0 ? T(paramRaw[i]) : defP[i];
            if (TitleRaw != null) Title = T(TitleRaw);
        }

        // 게임 폴더 안의 한국어 번역 데이터 찾기. 찾은 개수를 돌려줌
        public int FindGameTranslations()
        {
            gameTr.Clear(); locale.Clear();
            if (!IsJs) return 0;
            var files = new List<string>();
            try { Walk(Root, 0, files); } catch { }
            foreach (var f in files)
            {
                string low = f.ToLowerInvariant().Replace('\\', '/');
                try
                {
                    // ① 번역된 데이터 폴더 (data_ko/Items.json 같은 것)
                    if (Path.GetFileName(low) == "items.json" && !string.Equals(Path.GetDirectoryName(f), DataDir, StringComparison.OrdinalIgnoreCase))
                    {
                        string txt = File.ReadAllText(f, Encoding.UTF8);
                        if (HasHangul(txt)) PairDataDir(Path.GetDirectoryName(f));
                        continue;
                    }
                    // ② 키 → 번역 파일 (locales/ko.json, lang/kr.csv ...)
                    if (!IsLocalePath(low)) continue;
                    if (low.EndsWith(".json")) FlattenLocale(Json.Parse(File.ReadAllText(f, Encoding.UTF8)), "");
                    else if (low.EndsWith(".csv") || low.EndsWith(".tsv") || low.EndsWith(".txt")) ReadCsv(f);
                }
                catch { }
            }
            // 원문 그대로가 키인 경우
            foreach (var raw in AllRaw())
            {
                if (string.IsNullOrEmpty(raw) || gameTr.ContainsKey(raw)) continue;
                string v;
                if (locale.TryGetValue(raw, out v) && HasHangul(v)) gameTr[raw] = v;
                else if (Ph.IsMatch(raw)) { string r = Resolve(raw); if (r != raw && HasHangul(r)) gameTr[raw] = r; }
            }
            return gameTr.Count;
        }

        static bool IsLocalePath(string low)
        {
            bool langDir = new[] { "locale", "lang", "translat", "i18n", "locali", "korean", "한국" }.Any(low.Contains);
            string name = Path.GetFileNameWithoutExtension(low);
            bool ko = System.Text.RegularExpressions.Regex.IsMatch(low, @"(^|[/_\-.])(ko|kr|kor|korean|ko-kr|ko_kr|한국어|한글)([/_\-.]|$)");
            return langDir && ko;
        }

        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio", "movies", "img", "fonts", "node_modules", "effects", "swiftshader", "locales_chromium" };

        static void Walk(string dir, int depth, List<string> outFiles)
        {
            if (depth > 5 || outFiles.Count > 5000) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (e == ".json" || e == ".csv" || e == ".tsv" || e == ".txt") outFiles.Add(f);
            }
            foreach (var d in Directory.GetDirectories(dir))
            {
                string n = Path.GetFileName(d);
                if (SkipDirs.Contains(n) || n.StartsWith(".")) continue;
                if (depth == 0 && n.Equals("locales", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(dir, "nw.dll"))) continue; // NW.js 자체 언어 파일
                Walk(d, depth + 1, outFiles);
            }
        }

        void PairDataDir(string alt)
        {
            foreach (var pair in new[] { new { F = "Items.json", D = Items }, new { F = "Weapons.json", D = Weapons }, new { F = "Armors.json", D = Armors }, new { F = "Skills.json", D = Skills } })
            {
                var arr = ReadJson(Path.Combine(alt, pair.F)) as JArr;
                if (arr == null) continue;
                foreach (var n in arr.Items)
                {
                    DbItem it;
                    if (!(n is JObj) || !pair.D.TryGetValue((int)Json.Num(n, "id"), out it)) continue;
                    AddTr(it.RawName, Json.Str(n, "name")); AddTr(it.RawDesc, Json.Str(n, "description"));
                }
            }
            foreach (var pair in new[] { new { F = "Actors.json", D = Actors }, new { F = "Classes.json", D = Classes } })
            {
                var arr = ReadJson(Path.Combine(alt, pair.F)) as JArr;
                if (arr == null) continue;
                foreach (var n in arr.Items)
                {
                    JObj o;
                    if (!(n is JObj) || !pair.D.TryGetValue((int)Json.Num(n, "id"), out o)) continue;
                    AddTr(Json.Str(o, "name"), Json.Str(n, "name")); AddTr(Json.Str(o, "nickname"), Json.Str(n, "nickname"));
                }
            }
            var sys = ReadJson(Path.Combine(alt, "System.json"));
            if (sys == null) return;
            AddTr(TitleRaw, Json.Str(sys, "gameTitle"));
            PairArr(swRaw, Json.Arr(sys, "switches")); PairArr(varRaw, Json.Arr(sys, "variables"));
            PairArr(equipRaw, Json.Arr(sys, "equipTypes")); PairArr(paramRaw, Json.Arr(Json.Get(sys, "terms"), "params"));
        }

        static JNode ReadJson(string p) { return File.Exists(p) ? Json.Parse(File.ReadAllText(p, Encoding.UTF8)) : null; }

        void PairArr(string[] raw, JArr tr)
        {
            for (int i = 0; i < raw.Length; i++) { var v = Json.At(tr, i) as JVal; if (v != null && v.IsStr) AddTr(raw[i], v.Str); }
        }

        void AddTr(string raw, string ko)
        {
            if (!string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(ko) && raw != ko && HasHangul(ko)) gameTr[raw] = ko;
        }

        void FlattenLocale(JNode n, string prefix)
        {
            var o = n as JObj;
            if (o != null)
            {
                foreach (var k in o.Keys)
                {
                    var v = o.Map[k] as JVal;
                    if (v != null && v.IsStr) { locale[k] = v.Str; if (prefix.Length > 0) locale[prefix + k] = v.Str; }
                    else FlattenLocale(o.Map[k], prefix + k + ".");
                }
                return;
            }
            var a = n as JArr;
            if (a != null) foreach (var x in a.Items)
                {
                    // [{ "key": .., "ko": .. }] 모양
                    var xo = x as JObj;
                    if (xo == null) continue;
                    string key = new[] { "key", "id", "original", "source", "ja", "jp", "src" }.Select(kk => Json.Str(xo, kk)).FirstOrDefault(z => z.Length > 0);
                    string val = new[] { "ko", "kr", "korean", "translation", "text", "target", "value" }.Select(kk => Json.Str(xo, kk)).FirstOrDefault(z => HasHangul(z));
                    if (!string.IsNullOrEmpty(key) && val != null) locale[key] = val;
                }
        }

        void ReadCsv(string f)
        {
            foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
            {
                var cols = SplitCsv(line);
                if (cols.Count < 2 || cols[0].Length == 0) continue;
                var ko = cols.Skip(1).FirstOrDefault(HasHangul);
                if (ko != null) locale[cols[0]] = ko;
            }
        }

        static List<string> SplitCsv(string line)
        {
            var r = new List<string>();
            char sep = line.Contains('\t') ? '\t' : ',';
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == sep) { r.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            r.Add(sb.ToString());
            return r;
        }

        // ---------- 그림 ----------
        Bitmap LoadImage(string relNoExt)
        {
            string p = Path.Combine(ImgDir, relNoExt);
            try
            {
                if (IsRuby)
                {
                    foreach (var ext in new[] { ".png", ".jpg", ".bmp" })
                    {
                        var b = ReadGameFile("Graphics\\" + relNoExt.Replace('/', '\\') + ext);
                        if (b != null) return Detach(b);
                    }
                    return null;
                }
                if (File.Exists(p + ".png")) return Detach(File.ReadAllBytes(p + ".png"));
                foreach (var ext in new[] { ".rpgmvp", ".png_" })
                {
                    if (!File.Exists(p + ext)) continue;
                    var b = File.ReadAllBytes(p + ext);
                    if (b.Length < 32) continue;
                    var d = new byte[b.Length - 16];
                    Array.Copy(b, 16, d, 0, d.Length);
                    if (key != null) for (int i = 0; i < 16; i++) d[i] ^= key[i];
                    else
                    {
                        // 키가 없으면 PNG 머리말로 복원
                        byte[] hdr = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
                        Array.Copy(hdr, d, 16);
                    }
                    return Detach(d);
                }
            }
            catch { }
            return null;
        }

        static Bitmap Detach(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            using (var b = new Bitmap(ms))
                return new Bitmap(b);
        }

        public Image Icon(int index, int size)
        {
            var bmp = new Bitmap(size, size);
            if (index <= 0) return bmp;
            Bitmap src = iconSet;
            int cs = IsJs ? 32 : 24, sx, sy;
            if (Kind == RpgKind.XP)
            {
                // XP 는 아이콘이 그림 파일 하나씩
                if (index > xpIcons.Count) return bmp;
                string key = "Icons/" + xpIcons[index - 1];
                if (!faceSheets.TryGetValue(key, out src)) { src = LoadImage(Path.Combine("Icons", xpIcons[index - 1])); faceSheets[key] = src; }
                if (src == null) return bmp;
                sx = 0; sy = 0; cs = Math.Min(src.Width, src.Height);
            }
            else
            {
                if (!iconTried) { iconTried = true; iconSet = IsJs ? LoadImage(Path.Combine("system", "IconSet")) : LoadImage(Path.Combine("System", "Iconset")); }
                src = iconSet;
                if (src == null) return bmp;
                int perRow = Math.Max(1, src.Width / cs);
                sx = index % perRow * cs; sy = index / perRow * cs;
            }
            // 아이콘을 정수배로만 키워서 흐려지거나 일그러지지 않게
            int scale = size / cs, d = scale > 0 ? cs * scale : size, o = (size - d) / 2;
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = scale > 0 ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
                                                : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(src, new Rectangle(o, o, d, d), new Rectangle(sx, sy, cs, cs), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        public Image Face(string name, int index, int size)
        {
            var bmp = new Bitmap(size, size);
            if (string.IsNullOrEmpty(name)) return bmp;
            Bitmap sheet;
            if (!faceSheets.TryGetValue(name, out sheet)) { sheet = LoadImage(Path.Combine(IsJs ? "faces" : "Faces", name)); faceSheets[name] = sheet; }
            if (sheet == null) return bmp;
            int fw = sheet.Width / 4, fh = sheet.Height / 2;
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(sheet, new Rectangle(0, 0, size, size), new Rectangle(index % 4 * fw, index / 4 * fh, fw, fh), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        // ---------- 세이브 찾기 ----------
        public List<SaveSlot> FindSaves()
        {
            var list = new List<SaveSlot>();
            if (SaveDir == null || !Directory.Exists(SaveDir)) return list;
            string pattern;
            switch (Kind)
            {
                case RpgKind.MV: pattern = @"^file(\d+)\.rpgsave$"; break;
                case RpgKind.MZ: pattern = @"^file(\d+)\.rmmzsave$"; break;
                case RpgKind.VXAce: pattern = @"^save(\d+)\.rvdata2$"; break;
                case RpgKind.VX: pattern = @"^save(\d+)\.rvdata$"; break;
                case RpgKind.XP: pattern = @"^save(\d+)\.rxdata$"; break;
                case RpgKind.R2000: pattern = @"^save(\d+)\.lsd$"; break;
                default: pattern = @"^(?:file|save)(\d+)\.(rpgsave|rmmzsave|rvdata2|rvdata|rxdata|lsd)$"; break;
            }
            JArr global = null;
            if (IsJs)
            {
                try
                {
                    string gp = Path.Combine(SaveDir, Kind == RpgKind.MZ ? "global.rmmzsave" : "global.rpgsave");
                    SaveFmt f;
                    if (File.Exists(gp)) global = Json.Parse(SaveCodec.Read(gp, out f)) as JArr;
                }
                catch { }
            }
            foreach (var p in Directory.GetFiles(SaveDir))
            {
                var m = Regex.Match(Path.GetFileName(p), pattern, RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                int slot = int.Parse(m.Groups[1].Value);
                var s = new SaveSlot { Path = p, Slot = slot, Label = slot == 0 && Kind == RpgKind.MZ ? "자동 저장" : "파일 " + slot };
                s.When = File.GetLastWriteTime(p).ToString("MM-dd HH:mm");
                var info = Json.At(global, slot);
                if (info != null) s.PlayTime = Json.Str(info, "playtime");
                list.Add(s);
            }
            return list.OrderBy(x => x.Slot).ToList();
        }

        // ---------- 레벨·경험치 계산 ----------
        public double ExpFor(int actorId, int classId, int level)
        {
            JObj a;
            if (Kind == RpgKind.VX || Kind == RpgKind.XP)
            {
                if (!Actors.TryGetValue(actorId, out a) || level <= 1) return 0;
                double basis = Json.Num(a, "expBasis", 25), infl = Json.Num(a, "expInflation", 35), sum = 0;
                for (int i = 2; i <= level; i++)
                {
                    if (Kind == RpgKind.VX) { double n = 0.75 + infl / 200.0; sum += Math.Floor(basis * Math.Pow(i + 3, n) / Math.Pow(5, n)); }
                    else { double pw = 2.4 + infl / 100.0; sum += Math.Floor(basis * Math.Pow(i + 3, pw) / Math.Pow(5, pw)); }
                }
                return sum;
            }
            return ExpForLevel(classId, level);
        }

        public double ExpForLevel(int classId, int level)
        {
            JObj c;
            if (!Classes.TryGetValue(classId, out c) || level <= 1) return 0;
            var p = Json.Arr(c, "expParams");
            double basis = Json.Num(Json.At(p, 0), 30), extra = Json.Num(Json.At(p, 1), 20),
                accA = Json.Num(Json.At(p, 2), 30), accB = Json.Num(Json.At(p, 3), 30);
            return Math.Round(basis * Math.Pow(level - 1, 0.9 + accA / 250) * level * (level + 1)
                / (6 + Math.Pow(level, 2) / 50 / accB) + (level - 1) * extra, MidpointRounding.AwayFromZero);
        }

        public int BaseParam(int actorId, int classId, int param, int level)
        {
            JObj c;
            JArr table;
            if (Kind == RpgKind.VX || Kind == RpgKind.XP)
            {
                if (!Actors.TryGetValue(actorId, out c)) return 0;
                table = Json.Arr(c, "parameters");
            }
            else
            {
                if (!Classes.TryGetValue(classId, out c)) return 0;
                table = Json.Arr(c, "params");
            }
            var row = Json.Arr(Json.At(table, param));
            return (int)Json.Num(Json.At(row, Math.Min(level, row == null ? 0 : row.Items.Count - 1)));
        }

        public string ClassName(int classId)
        {
            JObj c;
            return Classes.TryGetValue(classId, out c) ? T(Json.Str(c, "name")) : "";
        }

        public int MaxLevel(int actorId)
        {
            JObj a;
            if (!Actors.TryGetValue(actorId, out a)) return 99;
            return (int)Json.Num(a, Kind == RpgKind.XP ? "finalLevel" : "maxLevel", 99);
        }
    }

    // 자동 백업: 내 문서\미니 치트\백업\게임이름\날짜 시각\파일
    static class Backup
    {
        public const int Keep = 30;

        public static string Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "미니 치트"); }
        }

        static string Safe(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim().Length == 0 ? "게임" : s.Trim();
        }

        public static string Dir(string game) { return Path.Combine(Path.Combine(Root, "백업"), Safe(game)); }

        public static void Make(string game, string file, string note)
        {
            if (!File.Exists(file)) return;
            string d = Path.Combine(Dir(game), DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + (note == null ? "" : " " + note));
            Directory.CreateDirectory(d);
            File.Copy(file, Path.Combine(d, Path.GetFileName(file)), true);
            Prune(game);
        }

        public static List<string[]> List(string game)
        {
            // { 시각 폴더 이름, 파일 이름, 전체 경로 }
            var r = new List<string[]>();
            string d = Dir(game);
            if (!Directory.Exists(d)) return r;
            foreach (var sub in Directory.GetDirectories(d).OrderByDescending(x => x))
                foreach (var f in Directory.GetFiles(sub))
                    r.Add(new[] { Path.GetFileName(sub), Path.GetFileName(f), f });
            return r;
        }

        static void Prune(string game)
        {
            var subs = Directory.GetDirectories(Dir(game)).OrderByDescending(x => x).ToList();
            for (int i = Keep; i < subs.Count; i++) try { Directory.Delete(subs[i], true); } catch { }
        }

        // 게임 목록 기억
        static string GamesFile { get { return Path.Combine(Root, "게임 목록.txt"); } }

        public static List<string> Games()
        {
            try { return File.Exists(GamesFile) ? File.ReadAllLines(GamesFile).Where(l => l.Trim().Length > 0 && Directory.Exists(l)).Distinct().ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        public static void RememberGame(string root)
        {
            try
            {
                var l = Games();
                l.RemoveAll(x => string.Equals(x, root, StringComparison.OrdinalIgnoreCase));
                l.Insert(0, root);
                Directory.CreateDirectory(Root);
                File.WriteAllLines(GamesFile, l.Take(50));
            }
            catch { }
        }
    }
}
