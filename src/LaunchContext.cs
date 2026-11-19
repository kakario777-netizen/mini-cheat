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

        static int ParentPid()
        {
            int me = Process.GetCurrentProcess().Id;
            IntPtr snap = CreateToolhelp32Snapshot(2, 0);
            if (snap == IntPtr.Zero || snap == (IntPtr)(-1)) return 0;
            try
            {
                var pe = new PE32 { dwSize = (uint)Marshal.SizeOf(typeof(PE32)) };
                if (!Process32FirstW(snap, ref pe)) return 0;
                do { if (pe.th32ProcessID == me) return (int)pe.th32ParentProcessID; }
                while (Process32NextW(snap, ref pe));
            }
            finally { CloseHandle(snap); }
            return 0;
        }

        static bool IsLauncher(int pid)
        {
            try { return Process.GetProcessById(pid).ProcessName.Equals("TkoolLauncher", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // 런처가 게임 중에 켰으면 팝업 명령("show\t폴더\tpid\tx\ty\tw\th"), 아니면 null
        public static string PanelCommand()
        {
            try
            {
                int parent = ParentPid();
                if (parent <= 0 || !IsLauncher(parent)) return null;
                uint fpid;
                GetWindowThreadProcessId(GetForegroundWindow(), out fpid);
                if (fpid == 0 || fpid == parent || fpid == Process.GetCurrentProcess().Id) return null; // 런처가 앞이면 전체 화면
                string exe = Native.ExePath((int)fpid);
                if (exe == null) return null;
                string name = Path.GetFileName(exe).ToLowerInvariant();
                if (name == "explorer.exe" || exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return null;
                string dir = Path.GetDirectoryName(exe);

                // 런처 팝업과 같은 자리: 오른쪽, 화면 가로 26% (최소 360 × 화면 배율)
                var sc = Screen.PrimaryScreen.Bounds;
                float k = 1f;
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
                int w = Math.Max((int)(360 * k), (int)(sc.Width * 0.26f));
                return "show\t" + dir + "\t" + fpid + "\t" + (sc.Right - w) + "\t" + sc.Top + "\t" + w + "\t" + sc.Height;
            }
            catch { return null; }
        }
    }
}
