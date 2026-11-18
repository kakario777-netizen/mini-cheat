using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MiniCheat
{
    static class Updater
    {
        const string Repo = "kakario777-netizen/mini-cheat";

        static string Exe { get { return Application.ExecutablePath; } }

        public static Version Current
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, v.Build);
            }
        }

        public static void Cleanup()
        {
            try { if (File.Exists(Exe + ".old")) File.Delete(Exe + ".old"); } catch { }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            var wc = new WebClient();
            wc.Headers.Add("User-Agent", "mini-cheat");
            wc.Encoding = Encoding.UTF8;
            return wc;
        }

        // 새 버전이 있으면 { 버전, 다운로드 주소 }, 없으면 null
        public static string[] FindNewer()
        {
            using (var wc = Client())
            {
                string j = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                var t = Regex.Match(j, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                var u = Regex.Match(j, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\"");
                if (!t.Success || !u.Success) return null;
                var nv = new Version(t.Groups[1].Value);
                nv = new Version(nv.Major, nv.Minor, Math.Max(0, nv.Build));
                return nv > Current ? new[] { t.Groups[1].Value, u.Groups[1].Value } : null;
            }
        }

        public static void Download(string url)
        {
            string nw = Exe + ".new";
            using (var wc = Client()) wc.DownloadFile(url, nw);
            if (new FileInfo(nw).Length < 10000) throw new Exception("받은 파일이 이상해요");
        }

        // 실행 중인 exe 는 이름만 바꿀 수 있어서: 지금 것 → .old, 새 것 → 원래 이름, 다시 켜기
        // 다시 켜지 않고 파일만 바꿔 둠 (팝업 모드 — 다음에 켤 때 새 버전)
        public static void SwapOnly()
        {
            string exe = Exe, nw = exe + ".new", old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            File.Move(nw, exe);
        }

        public static void SwapAndRestart()
        {
            string exe = Exe, nw = exe + ".new", old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            File.Move(nw, exe);
            Process.Start(exe);
            Environment.Exit(0);
        }
    }
}
