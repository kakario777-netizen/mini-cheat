using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MiniCheat
{
    // 게임기 모드: 손가락으로 누르기 쉽게 글자·버튼·줄 높이를 키우고, 숫자 칸은 큰 숫자판으로 입력
    static class Touch
    {
        public static bool On;
        public const float F = 1.5f;
        static readonly HashSet<Control> hooked = new HashSet<Control>();

        static string SettingsFile { get { return Path.Combine(Backup.Root, "설정.txt"); } }

        public static bool LoadSetting()
        {
            try { return File.Exists(SettingsFile) && File.ReadAllLines(SettingsFile).Any(l => l.Trim() == "게임기모드=1"); }
            catch { return false; }
        }

        public static void SaveSetting(bool on)
        {
            try
            {
                Directory.CreateDirectory(Backup.Root);
                var lines = File.Exists(SettingsFile) ? File.ReadAllLines(SettingsFile).Where(l => !l.StartsWith("게임기모드=")).ToList() : new List<string>();
                lines.Add("게임기모드=" + (on ? "1" : "0"));
                File.WriteAllLines(SettingsFile, lines);
            }
            catch { }
        }

        // 컨트롤 묶음 전체를 f 배 (위치·크기·글자·목록 칸)
        public static void Scale(Control root, float f)
        {
            root.SuspendLayout();
            ScaleFonts(root, f);
            if (root is Form) foreach (Control c in root.Controls) c.Scale(new SizeF(f, f));
            else root.Scale(new SizeF(f, f));
            ScaleLists(root, f);
            root.ResumeLayout(true);
        }

        // 먼저 모든 컨트롤의 원래 글자 크기를 기록한 뒤 한 번씩만 바꿈 (안쪽 컨트롤이 두 번 커지지 않게)
        static void ScaleFonts(Control root, float f)
        {
            var list = new List<KeyValuePair<Control, Font>>();
            Collect(root, list);
            foreach (var kv in list) kv.Key.Font = new Font(kv.Value.FontFamily, kv.Value.Size * f, kv.Value.Style);
        }

        static void Collect(Control c, List<KeyValuePair<Control, Font>> list)
        {
            list.Add(new KeyValuePair<Control, Font>(c, c.Font));
            foreach (Control k in c.Controls) Collect(k, list);
        }

        static void ScaleLists(Control c, float f)
        {
            var lv = c as ListView;
            if (lv != null)
            {
                foreach (ColumnHeader h in lv.Columns) h.Width = (int)(h.Width * f);
                // Details 목록은 줄 높이를 작은 그림 높이로 키움
                if (lv.View == View.Details && (lv.SmallImageList == null || lv.SmallImageList.Images.Count == 0))
                {
                    if (On) lv.SmallImageList = new ImageList { ImageSize = new Size(1, 34) };
                    else if (lv.SmallImageList != null && lv.SmallImageList.ImageSize.Width == 1) lv.SmallImageList = null;
                }
            }
            var g = c as DataGridView;
            if (g != null)
            {
                int h = On ? 38 : 22;
                g.RowTemplate.Height = h;
                foreach (DataGridViewRow r in g.Rows) r.Height = h;
                g.ColumnHeadersHeight = (int)(g.ColumnHeadersHeight * f);
            }
            var cb = c as ComboBox;
            if (cb != null && cb.DrawMode == DrawMode.OwnerDrawFixed) cb.ItemHeight = (int)(cb.ItemHeight * f);
            foreach (Control k in c.Controls) ScaleLists(k, f);
        }

        // 새로 만든 대화상자·컨트롤도 지금 모드에 맞게
        public static void Fit(Control c)
        {
            if (!On) return;
            Scale(c, F);
            Hook(c);
        }

        // 숫자 칸을 누르면 큰 숫자판
        public static void Hook(Control root)
        {
            var n = root as NumericUpDown;
            if (n != null && hooked.Add(n))
            {
                foreach (Control inner in n.Controls)
                    inner.MouseUp += (s, e) =>
                    {
                        if (!On || !(inner is TextBox)) return;
                        decimal? v = NumPad.Ask(n.FindForm(), (double)n.Value, n.Minimum < 0, n.DecimalPlaces > 0);
                        if (v.HasValue) n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v.Value));
                    };
            }
            var g = root as DataGridView;
            if (g != null && hooked.Add(g))
            {
                g.CellClick += (s, e) =>
                {
                    if (!On || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                    var cell = g.Rows[e.RowIndex].Cells[e.ColumnIndex];
                    if (cell.ReadOnly || cell.OwningColumn.ReadOnly || cell is DataGridViewCheckBoxCell) return;
                    double cur;
                    if (!double.TryParse(Convert.ToString(cell.Value), NumberStyles.Float, CultureInfo.InvariantCulture, out cur)) return;
                    decimal? v = NumPad.Ask(g.FindForm(), cur, true, true);
                    if (v.HasValue) cell.Value = JVal.Fmt((double)v.Value);
                };
            }
            foreach (Control k in root.Controls) Hook(k);
        }

        // 일반 글자 칸(검색 값 등)도 숫자판으로
        public static void HookNumberBox(TextBox t)
        {
            t.MouseUp += (s, e) =>
            {
                if (!On) return;
                double cur;
                double.TryParse(t.Text.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out cur);
                decimal? v = NumPad.Ask(t.FindForm(), cur, true, true);
                if (v.HasValue) t.Text = JVal.Fmt((double)v.Value);
            };
        }
    }

    // 큰 숫자판
    class NumPad : Form
    {
        readonly Label disp;
        string text;
        bool fresh = true;

        public static decimal? Ask(IWin32Window owner, double cur, bool allowNeg, bool allowDot)
        {
            using (var f = new NumPad(cur, allowNeg, allowDot))
            {
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                decimal d;
                return decimal.TryParse(f.text, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : (decimal?)null;
            }
        }

        NumPad(double cur, bool allowNeg, bool allowDot)
        {
            text = JVal.Fmt(cur);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("맑은 고딕", 20F, FontStyle.Bold);
            Text = "숫자 입력";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 560);
            KeyPreview = true;

            disp = new Label { Left = 12, Top = 12, Width = 396, Height = 70, TextAlign = ContentAlignment.MiddleRight, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, Font = new Font("맑은 고딕", 26F, FontStyle.Bold) };
            Controls.Add(disp);
            string[,] keys = { { "7", "8", "9" }, { "4", "5", "6" }, { "1", "2", "3" }, { allowNeg ? "±" : "", "0", allowDot ? "." : "" } };
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 3; c++)
                {
                    string k = keys[r, c];
                    if (k.Length == 0) continue;
                    var b = new Button { Text = k, Left = 12 + c * 134, Top = 94 + r * 92, Width = 126, Height = 84 };
                    b.Click += (s, e) => Press(k);
                    Controls.Add(b);
                }
            var del = new Button { Text = "← 지움", Left = 12, Top = 464, Width = 126, Height = 84, Font = new Font("맑은 고딕", 15F, FontStyle.Bold) };
            var no = new Button { Text = "취소", Left = 146, Top = 464, Width = 126, Height = 84, DialogResult = DialogResult.Cancel, Font = new Font("맑은 고딕", 15F, FontStyle.Bold) };
            var ok = new Button { Text = "확인", Left = 280, Top = 464, Width = 128, Height = 84, BackColor = Color.FromArgb(60, 130, 220), ForeColor = Color.White, Font = new Font("맑은 고딕", 15F, FontStyle.Bold) };
            del.Click += (s, e) => Press("del");
            ok.Click += (s, e) => { if (text == "" || text == "-") text = "0"; DialogResult = DialogResult.OK; };
            Controls.AddRange(new Control[] { del, no, ok });
            CancelButton = no;
            KeyPress += (s, e) =>
            {
                if (char.IsDigit(e.KeyChar) || (e.KeyChar == '.' && allowDot)) Press(e.KeyChar.ToString());
                else if (e.KeyChar == '\b') Press("del");
                else if (e.KeyChar == '\r') ok.PerformClick();
            };
            UpdateDisp();
        }

        void UpdateDisp() { disp.Text = text.Length == 0 ? "0" : text; }

        void Press(string k)
        {
            if (k == "del") { text = text.Length > 0 ? text.Substring(0, text.Length - 1) : ""; fresh = false; }
            else if (k == "±") text = text.StartsWith("-") ? text.Substring(1) : "-" + text;
            else
            {
                if (fresh) { text = ""; fresh = false; } // 처음 누르면 기존 값 대신 새로 입력
                if (k == "." && text.Contains(".")) return;
                if (text.Replace("-", "").Replace(".", "").Length >= 12) return;
                text = text == "0" && k != "." ? k : text + k;
            }
            UpdateDisp();
        }
    }
}
