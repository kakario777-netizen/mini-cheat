using System;
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
            Application.Run(new MainForm());
        }
    }
}
