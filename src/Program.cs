using System;
using System.Linq;
using System.Windows.Forms;

namespace MiniCheat
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Updater.Cleanup();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            // 쯔꾸르 런처가 게임 중에 켰으면(인수 없이도) 런처 팝업 자리에 팝업 모드로
            string autoCmd = args.Contains("--panel") ? null : LaunchContext.PanelCommand();
            if (args.Contains("--panel") || autoCmd != null)
            {
                // 게임 옆 팝업. 이미 켜져 있으면 명령만 넘기고 끝
                string cmd = autoCmd ?? CheatPanel.CommandFromArgs(args);
                if (CheatPanel.SendToRunning(cmd)) return;
                var main = new MainForm();
                var panel = new CheatPanel(main);
                panel.HandleCreated += (s, e) => panel.BeginInvoke((Action)(() => panel.Command(cmd)));
                // 새 버전이 있으면 받아서 파일만 바꿔 둠 (다음에 켤 때 적용)
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { var r = Updater.FindNewer(); if (r != null) { Updater.Download(r[1]); Updater.SwapOnly(); } } catch { }
                });
                var ctx = new ApplicationContext();
                panel.CreateControl();
                var h = panel.Handle; // 창 핸들을 만들어 명령을 받을 수 있게 (보이는 건 show 명령 때)
                Application.Run(ctx);
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
