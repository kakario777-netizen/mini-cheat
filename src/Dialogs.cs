using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MiniCheat
{
    class DBListView : ListView
    {
        public DBListView() { DoubleBuffered = true; }
    }

    static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string label, string def)
        {
            using (var f = new Form())
            {
                f.SuspendLayout();
                f.AutoScaleDimensions = new SizeF(96F, 96F);
                f.AutoScaleMode = AutoScaleMode.Dpi;
                f.Font = new Font("맑은 고딕", 9F);
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false; f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(300, 100);
                var lbl = new Label { Text = label, Left = 10, Top = 10, AutoSize = true };
                var txt = new TextBox { Left = 10, Top = 32, Width = 280, Text = def ?? "" };
                var ok = new Button { Text = "확인", Left = 130, Top = 65, Width = 75, Height = 26, DialogResult = DialogResult.OK };
                var no = new Button { Text = "취소", Left = 215, Top = 65, Width = 75, Height = 26, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, txt, ok, no });
                f.AcceptButton = ok; f.CancelButton = no;
                f.Shown += (s, e) => { txt.Focus(); txt.SelectAll(); };
                f.ResumeLayout(false);
                return f.ShowDialog(owner) == DialogResult.OK ? txt.Text : null;
            }
        }
    }

    class ProcessForm : Form
    {
        public Process Selected;
        ListView lv;
        TextBox txt;
        CheckBox chk;
        Process[] all = new Process[0];

        public ProcessForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("맑은 고딕", 9F);
            Text = "프로세스 선택";
            ClientSize = new Size(560, 460);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            var top = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 7, 8, 4) };
            txt = new TextBox { Dock = DockStyle.Fill };
            chk = new CheckBox { Text = "창 있는 프로그램만", Checked = true, Dock = DockStyle.Right, Width = 150, Padding = new Padding(10, 0, 0, 0) };
            var lbl = new Label { Text = "검색", Dock = DockStyle.Left, Width = 40, TextAlign = ContentAlignment.MiddleLeft };
            top.Controls.Add(txt); top.Controls.Add(chk); top.Controls.Add(lbl);

            lv = new DBListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            lv.Columns.Add("프로그램", 150);
            lv.Columns.Add("창 제목", 290);
            lv.Columns.Add("PID", 70);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var no = new Button { Text = "취소", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "선택", Width = 90, Height = 28 };
            var re = new Button { Text = "새로고침", Width = 90, Height = 28 };
            bottom.Controls.Add(no); bottom.Controls.Add(ok); bottom.Controls.Add(re);
            CancelButton = no;

            Controls.Add(lv); Controls.Add(bottom); Controls.Add(top);

            txt.TextChanged += (s, e) => Fill();
            chk.CheckedChanged += (s, e) => Fill();
            lv.DoubleClick += (s, e) => Ok();
            ok.Click += (s, e) => Ok();
            re.Click += (s, e) => LoadList();
            txt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (lv.Items.Count > 0) { lv.Items[0].Selected = true; Ok(); } } };

            LoadList();
            ResumeLayout(false);
        }

        void LoadList()
        {
            int me = Process.GetCurrentProcess().Id;
            all = Process.GetProcesses().Where(p => p.Id != me && p.Id > 4).ToArray();
            Fill();
        }

        void Fill()
        {
            string f = txt.Text.Trim().ToLowerInvariant();
            var items = new List<ListViewItem>();
            foreach (var p in all)
            {
                string title;
                try { title = p.MainWindowTitle ?? ""; } catch { title = ""; }
                if (chk.Checked && title.Length == 0) continue;
                string name = p.ProcessName;
                if (f.Length > 0 && !name.ToLowerInvariant().Contains(f) && !title.ToLowerInvariant().Contains(f)) continue;
                items.Add(new ListViewItem(new[] { name, title, p.Id.ToString() }) { Tag = p });
            }
            items.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Items.AddRange(items.ToArray());
            lv.EndUpdate();
        }

        void Ok()
        {
            if (lv.SelectedItems.Count == 0) return;
            Selected = (Process)lv.SelectedItems[0].Tag;
            DialogResult = DialogResult.OK;
        }
    }
}
