using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniCheat
{
    // 쯔꾸르 런처가 게임 중에 켰는지 알아보기 → 그러면 런처 팝업 자리에 팝업 모드로
    // (런처는 그냥 MiniCheat.exe 를 실행만 함 — 런처를 따로 고칠 필요 없게)
    static class LaunchContext
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PE32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PE32 pe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PE32 pe);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        public static bool FromLauncher; // 업데이트로 다시 켤 때 --launcher 를 넘겨 이어 가게

        // 프로세스 표: pid → (부모 pid, 이름)
        static System.Collections.Generic.Dictionary<uint, Tuple<uint, string>> Procs()
        {
            var d = new System.Collections.Generic.Dictionary<uint, Tuple<uint, string>>();
            IntPtr snap = CreateToolhelp32Snapshot(2, 0);
            if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return d;
            try
            {
                var pe = new PE32 { dwSize = (uint)Marshal.SizeOf(typeof(PE32)) };
                if (!Process32FirstW(snap, ref pe)) return d;
                do { d[pe.th32ProcessID] = Tuple.Create(pe.th32ParentProcessID, pe.szExeFile ?? ""); }
                while (Process32NextW(snap, ref pe));
            }
            finally { CloseHandle(snap); }
            return d;
        }

        static bool LauncherName(string n)
        {
            n = (n ?? "").ToLowerInvariant();
            return n.Contains("tkool") || n.Contains("쯔꾸르");
        }

        // 나를 켠 런처 pid (부모·할아버지까지 — 업데이트로 다시 켜진 경우도), 없으면 0
        static uint LauncherAncestor(System.Collections.Generic.Dictionary<uint, Tuple<uint, string>> ps, out string chain)
        {
            chain = "";
            uint cur = (uint)Process.GetCurrentProcess().Id;
            for (int i = 0; i < 4; i++)
            {
                Tuple<uint, string> t;
                if (!ps.TryGetValue(cur, out t)) break;
                uint par = t.Item1;
                Tuple<uint, string> pt;
                if (par == 0 || !ps.TryGetValue(par, out pt)) break;
                chain += " ← " + pt.Item2 + "(" + par + ")";
                if (LauncherName(pt.Item2)) return par;
                cur = par;
            }
            // 부모 기록이 끊겼어도 런처가 돌고 있으면 그걸로 (업데이트 재시작 뒤)
            bool flag = Array.IndexOf(Environment.GetCommandLineArgs(), "--launcher") >= 0;
            foreach (var kv in ps) if (LauncherName(kv.Value.Item2) && (flag || chain.ToLowerInvariant().Contains("minicheat"))) return kv.Key;
            return 0;
        }

        static bool BadExe(string exe)
        {
            if (exe == null) return true;
            string name = Path.GetFileName(exe).ToLowerInvariant();
            if (name == "explorer.exe" || name.Contains("minicheat") || LauncherName(name)) return true;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return exe.StartsWith(win, StringComparison.OrdinalIgnoreCase);
        }

        // 게임 창으로 볼 만한가 (보이고, 최소화 아님, 도구 창 아님, 충분히 큼)
        static bool GameLike(IntPtr h)
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return false;
            int ex = GetWindowLong(h, -20);
            if ((ex & 0x80) != 0) return false; // TOOLWINDOW
            int cloaked;
            if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            RECT r; if (!GetWindowRect(h, out r)) return false;
            return r.R - r.L >= 300 && r.B - r.T >= 200;
        }

        // 런처가 게임 중에 켰으면 팝업 명령("show\t폴더\tpid\tx\ty\tw\th"), 아니면 null
        public static string PanelCommand()
        {
            string why = "";
            try
            {
                var ps = Procs();
                string chain;
                uint launcher = LauncherAncestor(ps, out chain);
                why = "부모" + chain;
                if (launcher == 0) { Log(why + " → 런처 아님, 보통 화면"); return null; }
                FromLauncher = true;
                uint me = (uint)Process.GetCurrentProcess().Id;

                // 1) 앞 창이 게임이면 그것  2) 아니면(런처 가림막·팝업이 앞) 화면 위에서부터 첫 게임 창
                uint gpid = 0; string exe = null;
                IntPtr fg = GetForegroundWindow();
                uint fpid; GetWindowThreadProcessId(fg, out fpid);
                if (fpid != 0 && fpid != launcher && fpid != me && GameLike(fg))
                {
                    string e = Native.ExePath((int)fpid);
                    if (!BadExe(e)) { gpid = fpid; exe = e; }
                }
                if (gpid == 0)
                {
                    EnumWindows((h, l) =>
                    {
                        uint p; GetWindowThreadProcessId(h, out p);
                        if (p == 0 || p == launcher || p == me || !GameLike(h)) return true;
                        string e = Native.ExePath((int)p);
                        if (BadExe(e)) return true;
                        gpid = p; exe = e; return false;
                    }, IntPtr.Zero);
                }
                if (gpid == 0) { Log(why + " → 게임 창 없음, 보통 화면"); return null; }
                string dir = Path.GetDirectoryName(exe);

                // 런처 팝업과 같은 자리: 오른쪽, 화면 가로 26% (최소 360 × 화면 배율)
                var sc = Screen.PrimaryScreen.Bounds;
                float k = 1f;
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
                int w = Math.Max((int)(360 * k), (int)(sc.Width * 0.26f));
                Log(why + " → 게임 " + exe + " → 팝업");
                return "show\t" + dir + "\t" + gpid + "\t" + (sc.Right - w) + "\t" + sc.Top + "\t" + w + "\t" + sc.Height;
            }
            catch (Exception ex) { Log(why + " → 오류 " + ex.Message); return null; }
        }

        static void Log(string s)
        {
            try
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "미니 치트");
                Directory.CreateDirectory(d);
                string f = Path.Combine(d, "실행 기록.txt");
                if (File.Exists(f) && new FileInfo(f).Length > 100000) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("MM-dd HH:mm:ss") + " " + s + Environment.NewLine);
            }
            catch { }
        }
    }
}
