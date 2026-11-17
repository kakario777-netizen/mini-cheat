using System;
using System.IO;

namespace MiniCheat
{
    // 게임 폴더를 보고 쯔꾸르 버전을 알아냄. 반환값은 MainForm 형식 목록의 번호 (-1 = 모름)
    static class RpgDetect
    {
        public const int K2000 = 6, KRgss = 7, KMv = 8;

        public static int Detect(string exe, out string name)
        {
            name = null;
            if (string.IsNullOrEmpty(exe)) return -1;
            try
            {
                string d = Path.GetDirectoryName(exe);
                if (Path.GetFileName(exe).Equals("RPG_RT.exe", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(Path.Combine(d, "RPG_RT.ldb")))
                { name = "쯔꾸르 2000/2003"; return K2000; }

                string lib = "";
                string ini = Path.Combine(d, "Game.ini");
                if (File.Exists(ini))
                    foreach (var l in File.ReadAllLines(ini))
                        if (l.TrimStart().StartsWith("Library", StringComparison.OrdinalIgnoreCase)) lib = l.ToUpperInvariant();

                if (lib.Contains("RGSS1") || Has(d, "*.rgssad")) { name = "쯔꾸르 XP"; return KRgss; }
                if (lib.Contains("RGSS2") || Has(d, "*.rgss2a")) { name = "쯔꾸르 VX"; return KRgss; }
                if (lib.Contains("RGSS3") || Has(d, "*.rgss3a")) { name = "쯔꾸르 VX Ace"; return KRgss; }

                foreach (var sub in new[] { "", "www" })
                {
                    string js = Path.Combine(Path.Combine(d, sub), "js");
                    if (File.Exists(Path.Combine(js, "rmmz_core.js"))) { name = "쯔꾸르 MZ"; return KMv; }
                    if (File.Exists(Path.Combine(js, "rpg_core.js"))) { name = "쯔꾸르 MV"; return KMv; }
                }
            }
            catch { }
            return -1;
        }

        static bool Has(string d, string pattern)
        {
            try { return Directory.GetFiles(d, pattern).Length > 0; } catch { return false; }
        }
    }
}
