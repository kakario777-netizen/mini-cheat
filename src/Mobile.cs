using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MiniCheat
{
    // ───────── 게임기 모드: 휴대폰 앱처럼 페이지를 넘기며 쓰는 큰 화면 ─────────
    static class Th
    {
        public static float K = 1f;
        public static int S(float v) { return (int)Math.Round(v * K); }
        public static readonly Color Bg = Color.FromArgb(242, 243, 247), Card = Color.White, Line = Color.FromArgb(226, 228, 234),
            Text = Color.FromArgb(28, 30, 33), Sub = Color.FromArgb(107, 114, 128), Accent = Color.FromArgb(47, 111, 235),
            Press = Color.FromArgb(232, 236, 245), Green = Color.FromArgb(34, 160, 107), Red = Color.FromArgb(229, 72, 77);
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        public static Font F(float pt, bool bold = false)
        {
            string k = pt + (bold ? "b" : "");
            Font f;
            if (!fonts.TryGetValue(k, out f)) { f = new Font("맑은 고딕", pt, bold ? FontStyle.Bold : FontStyle.Regular); fonts[k] = f; }
            return f;
        }
        public static void Init(Control c) { using (var g = c.CreateGraphics()) K = g.DpiX / 96f; }
        public static int ColW(int width) { return Math.Min(S(760), width - S(24)); }
        public static string N(double v) { return v.ToString("#,0"); }
    }

    class MBtn : Button
    {
        public MBtn(string text, Color back, Color fore, float pt = 16)
        {
            Text = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = back; ForeColor = fore; Font = Th.F(pt, true); Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }
    }

    // 한 줄
    class MRow
    {
        public int H = 80;
        public bool Header, Center;
        public string Title = "", Sub;
        public Func<string> Right;
        public Color RightColor = Th.Accent;
        public Image Icon;
        public int IconSize = 48;
        public bool Circle, Chevron, Dim;
        public Func<bool> Toggle;
        public Action Tap;
        public Color? CenterColor;

        public void Draw(Graphics g, Rectangle r, bool pressed)
        {
            if (Header)
            {
                TextRenderer.DrawText(g, Title, Th.F(12, true), new Rectangle(r.X + Th.S(16), r.Y, r.Width - Th.S(32), r.Height - Th.S(6)), Th.Sub,
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }
            using (var b = new SolidBrush(pressed ? Th.Press : Th.Card)) g.FillRectangle(b, r);
            using (var p = new Pen(Th.Line)) g.DrawLine(p, r.X + Th.S(16), r.Bottom - 1, r.Right, r.Bottom - 1);
            if (Center)
            {
                TextRenderer.DrawText(g, Title, Th.F(16, true), r, CenterColor ?? Th.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            int x = r.X + Th.S(16), right = r.Right - Th.S(16);
            if (Icon != null)
            {
                int s = Th.S(IconSize), y = r.Y + (r.Height - s) / 2;
                var st = g.Save();
                if (Circle)
                {
                    using (var path = new GraphicsPath()) { path.AddEllipse(x, y, s, s); g.SetClip(path); }
                    using (var bb = new SolidBrush(Th.Press)) g.FillEllipse(bb, x, y, s, s);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                }
                else g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Icon, new Rectangle(x, y, s, s));
                g.Restore(st);
                x += s + Th.S(14);
            }
            if (Chevron)
            {
                TextRenderer.DrawText(g, "›", Th.F(22), new Rectangle(right - Th.S(20), r.Y, Th.S(24), r.Height), Th.Sub, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                right -= Th.S(30);
            }
            if (Toggle != null)
            {
                bool on = Toggle();
                int w = Th.S(64), h = Th.S(38), tx = right - w, ty = r.Y + (r.Height - h) / 2;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Round(new Rectangle(tx, ty, w, h), h / 2)) using (var b = new SolidBrush(on ? Th.Green : Color.FromArgb(205, 208, 214))) g.FillPath(b, path);
                int k = h - Th.S(6);
                g.FillEllipse(Brushes.White, on ? tx + w - k - Th.S(3) : tx + Th.S(3), ty + Th.S(3), k, k);
                g.SmoothingMode = SmoothingMode.None;
                right = tx - Th.S(12);
            }
            else if (Right != null)
            {
                string t = Right() ?? "";
                var f = Th.F(16, true);
                int tw = Math.Min(TextRenderer.MeasureText(t, f).Width, (right - x) / 2);
                TextRenderer.DrawText(g, t, f, new Rectangle(right - tw, r.Y, tw, r.Height), Dim ? Th.Sub : RightColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                right -= tw + Th.S(12);
            }
            var tf = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            Color tc = Dim ? Color.FromArgb(160, 165, 175) : Th.Text;
            if (string.IsNullOrEmpty(Sub))
                TextRenderer.DrawText(g, Title, Th.F(16), new Rectangle(x, r.Y, right - x, r.Height), tc, tf | TextFormatFlags.VerticalCenter);
            else
            {
                int mid = r.Y + r.Height / 2;
                TextRenderer.DrawText(g, Title, Th.F(16), new Rectangle(x, r.Y + Th.S(8), right - x, mid - r.Y - Th.S(6)), tc, tf | TextFormatFlags.Bottom);
                TextRenderer.DrawText(g, Sub, Th.F(12), new Rectangle(x, mid + Th.S(2), right - x, r.Bottom - mid - Th.S(10)), Th.Sub, tf | TextFormatFlags.Top);
            }
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // 손가락으로 끌어서 넘기는 목록 (관성 스크롤)
    class MList : Control
    {
        List<MRow> rows = new List<MRow>();
        float scroll;
        bool down, drag;
        int downY, pressIdx = -1;
        float downScroll, vel;
        readonly List<KeyValuePair<int, long>> track = new List<KeyValuePair<int, long>>();
        readonly Timer inertia = new Timer { Interval = 15 };
        readonly Stopwatch sw = Stopwatch.StartNew();

        public MList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Bg;
            inertia.Tick += (s, e) =>
            {
                scroll = Clamp(scroll - vel * 15);
                vel *= 0.95f;
                if (Math.Abs(vel) < 0.02f) inertia.Stop();
                Invalidate();
            };
        }

        public void SetRows(IEnumerable<MRow> r, bool keepScroll = true)
        {
            rows = r.ToList();
            if (!keepScroll) scroll = 0;
            scroll = Clamp(scroll);
            Invalidate();
        }

        int Total { get { return rows.Sum(r => Th.S(r.Header ? 48 : r.H)) + Th.S(24); } }
        float Clamp(float v) { return Math.Max(0, Math.Min(v, Math.Max(0, Total - Height))); }

        int RowAt(int y)
        {
            int cy = Th.S(8) - (int)scroll;
            for (int i = 0; i < rows.Count; i++)
            {
                int h = Th.S(rows[i].Header ? 48 : rows[i].H);
                if (y >= cy && y < cy + h) return i;
                cy += h;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Th.Bg);
            int w = Th.ColW(Width), x = (Width - w) / 2, y = Th.S(8) - (int)scroll;
            for (int i = 0; i < rows.Count; i++)
            {
                int h = Th.S(rows[i].Header ? 48 : rows[i].H);
                if (y + h > 0 && y < Height) rows[i].Draw(g, new Rectangle(x, y, w, h), i == pressIdx && !drag && !rows[i].Header);
                y += h;
            }
            int total = Total;
            if (total > Height && (drag || inertia.Enabled))
            {
                int bh = Math.Max(Th.S(40), Height * Height / total), by = (int)((Height - bh) * (scroll / (total - Height)));
                using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, x + w + Th.S(4), by, Th.S(5), bh);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            inertia.Stop();
            down = true; drag = false; downY = e.Y; downScroll = scroll;
            pressIdx = RowAt(e.Y);
            if (pressIdx >= 0 && (rows[pressIdx].Header || rows[pressIdx].Tap == null)) pressIdx = -1;
            track.Clear(); track.Add(new KeyValuePair<int, long>(e.Y, sw.ElapsedMilliseconds));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!down) return;
            int dy = e.Y - downY;
            if (!drag && Math.Abs(dy) > Th.S(10)) { drag = true; pressIdx = -1; }
            if (drag)
            {
                scroll = Clamp(downScroll - dy);
                track.Add(new KeyValuePair<int, long>(e.Y, sw.ElapsedMilliseconds));
                if (track.Count > 8) track.RemoveAt(0);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!down) return;
            down = false;
            if (drag)
            {
                long now = sw.ElapsedMilliseconds;
                var recent = track.Where(t => now - t.Value < 100).ToList();
                if (recent.Count >= 2)
                {
                    float dt = Math.Max(1, recent.Last().Value - recent.First().Value);
                    vel = (recent.Last().Key - recent.First().Key) / dt;
                    if (Math.Abs(vel) > 0.1f) inertia.Start();
                }
                drag = false;
                Invalidate();
                return;
            }
            int i = pressIdx;
            pressIdx = -1;
            Invalidate();
            if (i >= 0 && RowAt(e.Y) == i && rows[i].Tap != null) rows[i].Tap();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Clamp(scroll - e.Delta);
            Invalidate();
        }
    }

    // 위쪽 칸 나누기 버튼 (아이템/무기/방어구...)
    class MSegment : Control
    {
        public string[] Options;
        public int Selected;
        public event Action Changed;

        public MSegment(params string[] o)
        {
            Options = o;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Th.S(76); Dock = DockStyle.Top; BackColor = Th.Bg;
        }

        Rectangle Box { get { int w = Th.ColW(Width); return new Rectangle((Width - w) / 2, Th.S(10), w, Th.S(58)); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Th.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var b = Box;
            using (var p = MRow.Round(b, Th.S(12))) using (var br = new SolidBrush(Color.FromArgb(228, 230, 236))) g.FillPath(br, p);
            int w = b.Width / Options.Length;
            for (int i = 0; i < Options.Length; i++)
            {
                var r = new Rectangle(b.X + i * w + Th.S(4), b.Y + Th.S(4), w - Th.S(8), b.Height - Th.S(8));
                if (i == Selected) using (var p = MRow.Round(r, Th.S(9))) using (var br = new SolidBrush(Th.Accent)) g.FillPath(br, p);
                TextRenderer.DrawText(g, Options[i], Th.F(15, true), r, i == Selected ? Color.White : Th.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var b = Box;
            if (!b.Contains(e.Location)) return;
            int i = (e.X - b.X) * Options.Length / b.Width;
            if (i != Selected && i >= 0 && i < Options.Length) { Selected = i; Invalidate(); if (Changed != null) Changed(); }
        }
    }

    // 큰 검색 칸
    class MSearch : Panel
    {
        public readonly TextBox Box;
        public MSearch(string hint)
        {
            Dock = DockStyle.Top; Height = Th.S(76); BackColor = Th.Bg;
            Box = new TextBox { BorderStyle = BorderStyle.None, Font = Th.F(17), BackColor = Color.White };
            var holder = new Panel { BackColor = Color.White };
            var lbl = new Label { Text = "찾기", AutoSize = false, Width = Th.S(60), Dock = DockStyle.Left, Font = Th.F(15), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Th.Sub };
            holder.Controls.Add(Box); holder.Controls.Add(lbl);
            Controls.Add(holder);
            Resize += (s, e) =>
            {
                int w = Th.ColW(Width);
                holder.SetBounds((Width - w) / 2, Th.S(8), w, Th.S(60));
                Box.SetBounds(Th.S(64), (Th.S(60) - Box.PreferredHeight) / 2, w - Th.S(78), Box.PreferredHeight);
            };
            var tip = new ToolTip();
            tip.SetToolTip(Box, hint);
        }
    }

    // 숫자 고치기 팝업: 큰 숫자 + 올림/낮춤 버튼 (최대값에 맞춰 버튼 단위가 정해짐)
    class ValuePopup : Form
    {
        double val;
        readonly double min, max;
        readonly Label lblVal;

        public static double? Ask(IWin32Window owner, string title, double cur, double min, double max)
        {
            using (var f = new ValuePopup(title, cur, min, max))
                return f.ShowDialog(owner) == DialogResult.OK ? f.val : (double?)null;
        }

        ValuePopup(string title, double cur, double min, double max)
        {
            this.min = min; this.max = Math.Max(min, max);
            val = Math.Max(min, Math.Min(this.max, cur));
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            ShowInTaskbar = false;

            // 단위: 1, 10, 100 ... 중 (최대값/5) 이하, 큰 쪽 5개까지
            var steps = new List<double>();
            for (double s = 1; s <= Math.Max(1, (this.max - min) / 5); s *= 10) steps.Add(s);
            if (steps.Count > 5) steps = steps.Skip(steps.Count - 5).ToList();

            int pad = Th.S(20), bw = Th.S(118), bh = Th.S(76), gap = Th.S(10);
            int cols = Math.Max(steps.Count, 3);
            int w = pad * 2 + cols * bw + (cols - 1) * gap;
            int y = pad;
            var lblTitle = new Label { Text = title, Font = Th.F(16, true), ForeColor = Th.Sub, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(40)) };
            Controls.Add(lblTitle);
            y += Th.S(44);
            lblVal = new Label { Font = Th.F(34, true), ForeColor = Th.Text, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(80)), Cursor = Cursors.Hand, BackColor = Th.Bg };
            lblVal.Click += (s, e) =>
            {
                var v = NumPad.Ask(this, val, min < 0, false);
                if (v.HasValue) Set((double)v.Value);
            };
            Controls.Add(lblVal);
            y += Th.S(84);
            Controls.Add(new Label { Text = "숫자를 누르면 직접 입력 · 최대 " + Th.N(this.max), Font = Th.F(12), ForeColor = Th.Sub, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(30)) });
            y += Th.S(40);

            int rowX = pad + (cols - steps.Count) * (bw + gap) / 2;
            for (int i = 0; i < steps.Count; i++)
            {
                double st = steps[i];
                var up = new MBtn("+" + Unit(st), Color.FromArgb(225, 243, 234), Th.Green, 17) { Bounds = new Rectangle(rowX + i * (bw + gap), y, bw, bh) };
                var dn = new MBtn("−" + Unit(st), Color.FromArgb(252, 231, 231), Th.Red, 17) { Bounds = new Rectangle(rowX + i * (bw + gap), y + bh + gap, bw, bh) };
                up.Click += (s, e) => Set(val + st);
                dn.Click += (s, e) => Set(val - st);
                Controls.Add(up); Controls.Add(dn);
            }
            y += bh * 2 + gap * 2;
            int half = (w - pad * 2 - gap) / 2;
            var b0 = new MBtn(min < 0 ? "0" : "최소 (" + Th.N(min) + ")", Th.Bg, Th.Text, 15) { Bounds = new Rectangle(pad, y, half, bh) };
            var bMax = new MBtn("최대 (" + Th.N(this.max) + ")", Th.Bg, Th.Text, 15) { Bounds = new Rectangle(pad + half + gap, y, half, bh) };
            b0.Click += (s, e) => Set(min < 0 ? 0 : min);
            bMax.Click += (s, e) => Set(this.max);
            Controls.Add(b0); Controls.Add(bMax);
            y += bh + gap * 2;
            var no = new MBtn("취소", Th.Bg, Th.Text) { Bounds = new Rectangle(pad, y, half, Th.S(84)), DialogResult = DialogResult.Cancel };
            var ok = new MBtn("확인", Th.Accent, Color.White) { Bounds = new Rectangle(pad + half + gap, y, half, Th.S(84)), DialogResult = DialogResult.OK };
            Controls.Add(no); Controls.Add(ok);
            CancelButton = no; AcceptButton = ok;
            y += Th.S(84) + pad;
            ClientSize = new Size(w, y);
            Set(val);
        }

        void Set(double v) { val = Math.Max(min, Math.Min(max, Math.Round(v))); lblVal.Text = Th.N(val); }

        // 버튼 글자가 넘치지 않게 천·만·억 단위로
        static string Unit(double s)
        {
            if (s >= 1e8) return (s / 1e8) + "억";
            if (s >= 1e4) return (s / 1e4) + "만";
            if (s >= 1e3) return (s / 1e3) + "천";
            return s.ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Th.Line, 2)) e.Graphics.DrawRectangle(p, 1, 1, Width - 2, Height - 2);
        }
    }

    // 큰 버튼 질문창
    static class MDialog
    {
        public static int Choose(IWin32Window owner, string message, params string[] options)
        {
            using (var f = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterParent, BackColor = Color.White, ShowInTaskbar = false })
            {
                int pad = Th.S(22), w = Th.S(520), y = pad, result = -1;
                var lbl = new Label { Text = message, Font = Th.F(16, true), ForeColor = Th.Text, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
                int th = TextRenderer.MeasureText(message, lbl.Font, new Size(w - pad * 2, 1000), TextFormatFlags.WordBreak).Height + Th.S(16);
                lbl.Bounds = new Rectangle(pad, y, w - pad * 2, th);
                f.Controls.Add(lbl);
                y += th + Th.S(10);
                for (int i = 0; i < options.Length; i++)
                {
                    int idx = i;
                    var b = new MBtn(options[i], i == 0 ? Th.Accent : Th.Bg, i == 0 ? Color.White : Th.Text) { Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(78)) };
                    b.Click += (s, e) => { result = options[idx] == "취소" ? -1 : idx; f.Close(); };
                    f.Controls.Add(b);
                    y += Th.S(88);
                }
                f.ClientSize = new Size(w, y + pad - Th.S(10));
                f.Paint += (s, e) => { using (var p = new Pen(Th.Line, 2)) e.Graphics.DrawRectangle(p, 1, 1, f.Width - 2, f.Height - 2); };
                f.KeyPreview = true;
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
                f.ShowDialog(owner);
                return result;
            }
        }

        public static void Info(IWin32Window owner, string message) { Choose(owner, message, "확인"); }
    }

    // ───────── 페이지 틀 ─────────
    class MPage : Panel
    {
        public MobileForm M;
        public string Title = "";
        public string RightText;
        public Action RightAction;
        public string BottomText;
        public Action BottomAction;
        public Func<bool> BottomEnabled;
        public readonly MList List = new MList { Dock = DockStyle.Fill };

        public MPage() { BackColor = Th.Bg; }
        public virtual void Build() { Controls.Add(List); }
        public virtual void OnShow() { }
        public virtual bool CanLeave() { return true; }
    }

    // 세이브 편집 페이지들은 아래에 '저장하기' 바가 늘 붙음
    class SavePage : MPage
    {
        public SaveModel Model;
        public SavePage(SaveModel m)
        {
            Model = m;
            BottomText = "저장하기";
            BottomEnabled = () => Model.Dirty;
            BottomAction = () => { M.SaveModelNow(Model); OnShow(); };
        }
        protected void Changed() { OnShow(); M.Chrome(); }
    }

    class MobileForm : Form
    {
        public readonly MainForm Main;
        readonly Stack<MPage> stack = new Stack<MPage>();
        readonly Panel header, bottom, host;
        readonly MBtn btnBack, btnRight, btnBottom;
        readonly Label lblTitle, toast;
        readonly Timer toastT = new Timer { Interval = 2200 };
        public bool BackToPc;

        public MobileForm(MainForm main)
        {
            Main = main;
            Th.Init(this);
            Text = "미니 치트 — 게임기 모드";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = Th.Bg;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Th.S(1100), Th.S(720));
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            header = new Panel { Dock = DockStyle.Top, Height = Th.S(80), BackColor = Color.White };
            btnBack = new MBtn("‹", Color.White, Th.Accent, 30) { Dock = DockStyle.Left, Width = Th.S(84) };
            btnRight = new MBtn("", Color.White, Th.Accent, 15) { Dock = DockStyle.Right, Width = Th.S(150) };
            lblTitle = new Label { Dock = DockStyle.Fill, Font = Th.F(19, true), ForeColor = Th.Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(Th.S(8), 0, 0, 0) };
            header.Controls.Add(lblTitle); header.Controls.Add(btnBack); header.Controls.Add(btnRight);
            header.Paint += (s, e) => { using (var p = new Pen(Th.Line)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
            btnBack.Click += (s, e) => Pop();
            btnRight.Click += (s, e) => { var p = stack.Peek(); if (p.RightAction != null) p.RightAction(); };

            bottom = new Panel { Dock = DockStyle.Bottom, Height = Th.S(104), BackColor = Th.Bg };
            btnBottom = new MBtn("", Th.Accent, Color.White, 18);
            bottom.Controls.Add(btnBottom);
            bottom.Resize += (s, e) => { int w = Th.ColW(bottom.Width); btnBottom.SetBounds((bottom.Width - w) / 2, Th.S(10), w, Th.S(84)); };
            btnBottom.Click += (s, e) => { var p = stack.Peek(); if (p.BottomAction != null) p.BottomAction(); Chrome(); };

            host = new Panel { Dock = DockStyle.Fill, BackColor = Th.Bg };
            toast = new Label { AutoSize = false, Visible = false, BackColor = Color.FromArgb(40, 44, 52), ForeColor = Color.White, Font = Th.F(15, true), TextAlign = ContentAlignment.MiddleCenter };
            toastT.Tick += (s, e) => { toastT.Stop(); toast.Visible = false; };

            Controls.Add(host); Controls.Add(bottom); Controls.Add(header); Controls.Add(toast);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back && !(ActiveControl is TextBox)) Pop(); };
            Push(new HomePage());
        }

        public void Push(MPage p)
        {
            if (stack.Count > 0) stack.Peek().Visible = false;
            p.M = this;
            p.Dock = DockStyle.Fill;
            host.Controls.Add(p);
            stack.Push(p);
            p.Build();
            p.OnShow();
            Chrome();
        }

        public void Pop()
        {
            if (stack.Count <= 1) return;
            var p = stack.Peek();
            if (!p.CanLeave()) return;
            stack.Pop();
            host.Controls.Remove(p);
            p.Dispose();
            var t = stack.Peek();
            t.Visible = true;
            t.OnShow();
            Chrome();
        }

        public void Chrome()
        {
            if (stack.Count == 0) return;
            var p = stack.Peek();
            lblTitle.Text = p.Title;
            btnBack.Visible = stack.Count > 1;
            lblTitle.Padding = new Padding(stack.Count > 1 ? 0 : Th.S(20), 0, 0, 0);
            btnRight.Visible = p.RightText != null;
            btnRight.Text = p.RightText ?? "";
            bottom.Visible = p.BottomText != null;
            btnBottom.Text = p.BottomText ?? "";
            bool en = p.BottomEnabled == null || p.BottomEnabled();
            btnBottom.Enabled = en;
            btnBottom.BackColor = en ? Th.Accent : Color.FromArgb(190, 198, 214);
        }

        public void Toast(string text)
        {
            int w = Math.Min(Th.S(640), Width - Th.S(40)), h = Th.S(72);
            toast.Text = text;
            toast.SetBounds((ClientSize.Width - w) / 2, ClientSize.Height - h - Th.S(130), w, h);
            toast.Visible = true;
            toast.BringToFront();
            toastT.Stop(); toastT.Start();
        }

        public void SaveModelNow(SaveModel m)
        {
            if (!m.Dirty) return;
            if (m.ChangedOnDisk && MDialog.Choose(this, "그 사이에 게임이 이 세이브를 다시 저장했어요.\n그래도 덮어쓸까요? (원래 파일은 백업돼요)", "덮어쓰기", "취소") != 0) return;
            try { m.Save(); Toast("저장했어요! 게임에서 이 세이브를 다시 불러오세요"); }
            catch (Exception ex) { MDialog.Info(this, "저장하지 못했어요\n" + ex.Message); }
            Chrome();
        }

        // 번역: 게임 폴더 번역 파일 → 게임 내장 번역 → 자동 번역
        public async void Translate(GameData g, Action done)
        {
            if (!g.CanEdit) return;
            g.ApplyLang(true);
            if (g.SavedCoversAll()) { done(); return; }
            try
            {
                await Task.Run(() => g.FindGameTranslations());
                g.ApplyLang(true); done();
                var texts = g.TextsForAuto().ToList();
                if (Translator.CountMissing(texts) > 0)
                {
                    Toast("한글로 번역하는 중...");
                    await Task.Run(() => Translator.Translate(texts, null));
                }
                g.ApplyLang(true);
                g.SaveTrToGame();
                done();
            }
            catch { g.ApplyLang(true); done(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!BackToPc)
            {
                var sp = stack.OfType<SavePage>().FirstOrDefault(p => p.Model.Dirty);
                if (sp != null)
                {
                    int r = MDialog.Choose(this, "저장하지 않은 내용이 있어요", "저장하고 끄기", "저장 안 하고 끄기", "취소");
                    if (r < 0 || r == 2) { e.Cancel = true; return; }
                    if (r == 0) SaveModelNow(sp.Model);
                }
            }
            base.OnFormClosing(e);
        }
    }

    // ───────── 페이지들 ─────────
    class HomePage : MPage
    {
        public HomePage() { Title = "미니 치트"; }
        public override void OnShow()
        {
            List.SetRows(new[]
            {
                new MRow { Header = true, Title = "무엇을 할까요?" },
                new MRow { H = 100, Title = "세이브 수정", Sub = "돈 · 아이템 · 레벨 · 스위치를 바로 고쳐요", Chevron = true, Tap = () => M.Push(new GamesPage()) },
                new MRow { H = 100, Title = "메모리 검색", Sub = "게임을 켠 채로 숫자를 찾아 바꿔요", Chevron = true, Tap = () => M.Push(new MemPage()) },
                new MRow { Header = true, Title = "설정" },
                new MRow { Title = "PC 화면으로 돌아가기", Chevron = true, Tap = () => M.Main.SetGameMode(false) },
                new MRow { Title = "업데이트 확인", Right = () => "v" + Updater.Current, Tap = () => M.Main.CheckUpdatePublic() },
            });
        }
    }

    class GamesPage : MPage
    {
        public GamesPage() { Title = "게임 고르기"; }
        public override void OnShow()
        {
            var rows = new List<MRow>();
            string running = M.Main.RunningGameDir;
            if (running != null)
            {
                rows.Add(new MRow { Header = true, Title = "지금 고른 게임" });
                rows.Add(GameRow(running));
            }
            rows.Add(new MRow { Header = true, Title = "게임 목록" });
            foreach (var r in Backup.Games()) if (r != running) rows.Add(GameRow(r));
            rows.Add(new MRow { Center = true, Title = "+ 게임 폴더 추가", Tap = Add });
            List.SetRows(rows);
        }

        MRow GameRow(string root)
        {
            var kind = GameData.DetectKind(root);
            string kn = new GameData { Kind = kind }.KindName;
            return new MRow { H = 92, Title = Translator.Or(SaveEditor.QuickTitle(root)), Sub = kn + " · " + Path.GetFileName(root.TrimEnd('\\', '/')), Chevron = true, Tap = () => Open(root) };
        }

        void Add()
        {
            using (var f = new OpenFileDialog { Title = "게임 폴더 안의 Game.exe (또는 실행 파일)를 골라 주세요", Filter = "게임 실행 파일|*.exe|모든 파일|*.*" })
                if (f.ShowDialog(this) == DialogResult.OK) Open(Path.GetDirectoryName(f.FileName));
        }

        void Open(string root)
        {
            GameData g;
            try { g = GameData.Load(root); }
            catch (Exception ex) { MDialog.Info(M, "게임을 열지 못했어요\n" + ex.Message); return; }
            Backup.RememberGame(root);
            M.Push(new SlotsPage(g));
        }
    }

    class SlotsPage : MPage
    {
        readonly GameData db;
        List<SaveSlot> slots = new List<SaveSlot>();
        public SlotsPage(GameData g)
        {
            db = g; Title = g.Title;
            RightText = "백업"; RightAction = () => M.Push(new BackupsPage(db, slots));
        }
        public override void Build()
        {
            base.Build();
            M.Translate(db, () => { if (!IsDisposed) { Title = db.Title; OnShow(); M.Chrome(); } });
        }
        public override void OnShow()
        {
            Title = db.Title;
            slots = db.FindSaves();
            var rows = new List<MRow> { new MRow { Header = true, Title = db.KindName + " · 세이브 " + slots.Count + "개" } };
            foreach (var s in slots)
            {
                var sl = s;
                rows.Add(new MRow
                {
                    H = 92, Title = s.Label, Chevron = true,
                    Sub = (string.IsNullOrEmpty(s.PlayTime) ? "" : "플레이 " + s.PlayTime + "  ·  ") + "저장 " + s.When,
                    Tap = () => Open(sl)
                });
            }
            if (slots.Count == 0) rows.Add(new MRow { Title = "아직 세이브가 없어요. 게임에서 한 번 저장해 주세요", Dim = true });
            if (!db.CanEdit) rows.Add(new MRow { Title = "이 게임은 세이브 수정을 아직 못 해요 (백업·복원은 돼요)", Dim = true });
            List.SetRows(rows);
        }
        void Open(SaveSlot s)
        {
            if (!db.CanEdit) { MDialog.Info(M, "이 게임은 세이브 수정을 아직 못 해요.\n오른쪽 위 '백업'에서 백업·복원은 할 수 있어요."); return; }
            try { M.Push(new SaveHomePage(SaveModel.Open(db, s))); }
            catch (Exception ex) { MDialog.Info(M, "세이브를 읽지 못했어요\n" + ex.Message); }
        }
    }

    class BackupsPage : MPage
    {
        readonly GameData db;
        readonly List<SaveSlot> slots;
        public BackupsPage(GameData g, List<SaveSlot> s)
        {
            db = g; slots = s; Title = "백업 · 복원";
            RightText = "지금 백업";
            RightAction = () => { foreach (var sl in slots) Backup.Make(db.BackupName, sl.Path, "수동"); M.Toast("세이브 " + slots.Count + "개를 백업했어요"); OnShow(); };
        }
        public override void OnShow()
        {
            var rows = new List<MRow> { new MRow { Header = true, Title = "누르면 그 때 상태로 되돌려요 (최근 " + Backup.Keep + "개 보관)" } };
            foreach (var b in Backup.List(db.BackupName))
            {
                var bb = b;
                rows.Add(new MRow { Title = b[1], Sub = b[0], Tap = () => Restore(bb) });
            }
            if (rows.Count == 1) rows.Add(new MRow { Title = "아직 백업이 없어요", Dim = true });
            List.SetRows(rows);
        }
        void Restore(string[] b)
        {
            if (MDialog.Choose(M, b[1] + " 을(를)\n" + b[0] + " 상태로 되돌릴까요?", "되돌리기", "취소") != 0) return;
            try
            {
                string dst = Path.Combine(db.SaveDir, b[1]);
                string tmp = Path.GetTempFileName();
                File.Copy(b[2], tmp, true);
                if (File.Exists(dst)) Backup.Make(db.BackupName, dst, "되돌리기 전");
                Directory.CreateDirectory(db.SaveDir);
                File.Copy(tmp, dst, true);
                File.Delete(tmp);
                M.Toast("되돌렸어요. 게임에서 다시 불러오세요");
                OnShow();
            }
            catch (Exception ex) { MDialog.Info(M, "되돌리지 못했어요\n" + ex.Message); }
        }
    }

    class SaveHomePage : SavePage
    {
        public SaveHomePage(SaveModel m) : base(m) { Title = m.Slot.Label; }
        public override void OnShow()
        {
            var db = Model.Db;
            List.SetRows(new[]
            {
                new MRow { Header = true, Title = db.Title },
                new MRow { H = 92, Title = "돈", Right = () => Th.N(Model.Gold) + " " + db.Currency, Tap = () =>
                    { var v = ValuePopup.Ask(M, "돈", Model.Gold, 0, Model.MaxGold); if (v.HasValue) { Model.Gold = v.Value; Changed(); } } },
                new MRow { Title = "파티 (캐릭터)", Right = () => Model.PartyIds().Count + "명", Chevron = true, Tap = () => M.Push(new PartyPage(Model)) },
                new MRow { Title = "가방 (아이템 · 장비)", Chevron = true, Tap = () => M.Push(new BagPage(Model)) },
                new MRow { Title = "스위치", Sub = "켜고 끄기", Chevron = true, Tap = () => M.Push(new FlagsPage(Model, true)) },
                new MRow { Title = "변수", Sub = "숫자 바꾸기", Chevron = true, Tap = () => M.Push(new FlagsPage(Model, false)) },
                new MRow { Header = true, Title = "기록" },
                new MRow { Title = "플레이 시간", Right = () => { double s = Model.PlaySeconds; return (long)(s / 3600) + "시간 " + (long)(s / 60 % 60) + "분"; }, Tap = () =>
                    { double s = Model.PlaySeconds; var v = ValuePopup.Ask(M, "플레이 시간 (시간)", Math.Floor(s / 3600), 0, 9999); if (v.HasValue) { Model.PlaySeconds = v.Value * 3600 + s % 3600; Changed(); } } },
                new MRow { Title = "걸음 수", Right = () => Th.N(Model.Steps), Tap = () =>
                    { var v = ValuePopup.Ask(M, "걸음 수", Model.Steps, 0, 9999999); if (v.HasValue) { Model.Steps = v.Value; Changed(); } } },
            });
        }
        public override bool CanLeave()
        {
            if (!Model.Dirty) return true;
            int r = MDialog.Choose(M, "저장하지 않은 내용이 있어요", "저장하고 나가기", "저장 안 하고 나가기", "취소");
            if (r == 0) { M.SaveModelNow(Model); return !Model.Dirty; }
            return r == 1;
        }
    }

    class PartyPage : SavePage
    {
        public PartyPage(SaveModel m) : base(m) { Title = "파티"; }
        public override void OnShow()
        {
            var rows = new List<MRow>();
            foreach (var a in Model.Actors())
            {
                var ac = a;
                bool inP = Model.InParty(a);
                rows.Add(new MRow
                {
                    H = 100, Icon = Model.Db.Face(Json.Str(a, "_faceName"), (int)Json.Num(a, "_faceIndex"), Th.S(72)), IconSize = 72, Circle = true,
                    Title = Model.Name(a) + (inP ? "" : "  (대기)"), Dim = !inP,
                    Sub = "Lv " + Model.Level(a) + "  ·  HP " + Th.N(Model.Hp(a)) + (Model.ClassName(a).Length > 0 ? "  ·  " + Model.ClassName(a) : ""),
                    Chevron = true, Tap = () => M.Push(new ActorPage(Model, ac))
                });
            }
            List.SetRows(rows);
        }
    }

    class ActorPage : SavePage
    {
        readonly JObj a;
        public ActorPage(SaveModel m, JObj actor) : base(m) { a = actor; Title = m.Name(actor); }

        void Num(string title, double cur, double min, double max, Action<double> set)
        {
            var v = ValuePopup.Ask(M, title, cur, min, max);
            if (v.HasValue) { set(v.Value); Changed(); }
        }

        public override void OnShow()
        {
            var db = Model.Db;
            string mpName = db.Kind == RpgKind.XP ? "SP" : "MP";
            var rows = new List<MRow>
            {
                new MRow { H = 140, Icon = db.Face(Json.Str(a, "_faceName"), (int)Json.Num(a, "_faceIndex"), Th.S(112)), IconSize = 112, Circle = true,
                    Title = Model.Name(a), Sub = Model.ClassName(a) },
                new MRow { Header = true, Title = "기본" },
                new MRow { Title = "레벨", Right = () => Model.Level(a).ToString(), Tap = () => Num("레벨", Model.Level(a), 1, Model.MaxLevel(a), v => Model.SetLevel(a, (int)v)) },
                new MRow { Title = "HP", Right = () => Th.N(Model.Hp(a)) + " / " + Th.N(Model.Total(a, 0)), Tap = () => Num("HP", Model.Hp(a), 0, Math.Max(Model.Total(a, 0), Model.Hp(a)), v => Model.SetHp(a, v)) },
                new MRow { Title = mpName, Right = () => Th.N(Model.Mp(a)) + " / " + Th.N(Model.Total(a, 1)), Tap = () => Num(mpName, Model.Mp(a), 0, Math.Max(Model.Total(a, 1), Model.Mp(a)), v => Model.SetMp(a, v)) },
                new MRow { Center = true, Title = "HP · " + mpName + " 가득 채우기", Tap = () => { Model.SetHp(a, Model.Total(a, 0)); Model.SetMp(a, Model.Total(a, 1)); Changed(); M.Toast("가득 채웠어요"); } },
                new MRow { Header = true, Title = "능력치  (기본 + 추가)" },
            };
            for (int i = 0; i < db.ParamCount; i++)
            {
                int k = i;
                rows.Add(new MRow
                {
                    Title = db.ParamNames[i], Sub = "기본 " + Th.N(Model.Base(a, i)) + "  ·  추가 +" + Th.N(Model.Plus(a, i)),
                    Right = () => Th.N(Model.Total(a, k)),
                    Tap = () => Num(db.ParamNames[k] + " 추가", Model.Plus(a, k), 0, 9999, v => Model.SetPlus(a, k, v))
                });
            }
            rows.Add(new MRow { Header = true, Title = "장비" });
            foreach (var sl in Model.Equips(a))
            {
                var slot = sl;
                rows.Add(new MRow
                {
                    Icon = sl.Cur != null ? db.Icon(sl.Cur.Icon, Th.S(48)) : null, IconSize = 48, Title = sl.Cur != null ? sl.Cur.Name : "(없음)", Dim = sl.Cur == null,
                    Sub = Model.EquipName(sl.EType), Chevron = true,
                    Tap = () => M.Push(new PickPage(Model, Model.EquipName(slot.EType) + " 고르기", Model.EquipChoices(slot.EType), true, it => { slot.Set(it); }))
                });
            }
            var skills = Model.Skills(a);
            rows.Add(new MRow { Header = true, Title = "배운 스킬 " + skills.Count + "개  (누르면 잊기)" });
            foreach (var id in skills)
            {
                DbItem s;
                db.Skills.TryGetValue(id, out s);
                int sid = id;
                string nm = s == null ? "스킬 #" + id : s.Name;
                rows.Add(new MRow
                {
                    H = 72, Icon = db.Icon(s == null ? 0 : s.Icon, Th.S(44)), IconSize = 44, Title = nm,
                    Tap = () => { if (MDialog.Choose(M, nm + "\n이 스킬을 잊게 할까요?", "잊게 하기", "취소") == 0) { Model.RemoveSkill(a, sid); Changed(); } }
                });
            }
            rows.Add(new MRow
            {
                Center = true, Title = "+ 스킬 배우기",
                Tap = () => M.Push(new PickPage(Model, "스킬 배우기", db.Skills.Values.Where(x => !Model.Skills(a).Contains(x.Id)), false, it => { if (it != null) Model.AddSkill(a, it.Id); }))
            });
            List.SetRows(rows);
            Title = Model.Name(a);
        }
    }

    // 목록에서 하나 고르기 (장비·스킬)
    class PickPage : SavePage
    {
        readonly List<DbItem> items;
        readonly bool allowNone;
        readonly Action<DbItem> pick;
        MSearch search;
        public PickPage(SaveModel m, string title, IEnumerable<DbItem> items, bool allowNone, Action<DbItem> pick) : base(m)
        {
            Title = title; this.items = items.OrderBy(x => x.Id).ToList(); this.allowNone = allowNone; this.pick = pick;
        }
        public override void Build()
        {
            base.Build();
            search = new MSearch("이름으로 찾기");
            Controls.Add(search);
            search.Box.TextChanged += (s, e) => OnShow();
        }
        public override void OnShow()
        {
            string f = search == null ? "" : search.Box.Text.Trim();
            var rows = new List<MRow>();
            if (allowNone) rows.Add(new MRow { Title = "(없음) — 빼기", Dim = true, Tap = () => Done(null) });
            foreach (var it in items)
            {
                if (f.Length > 0 && it.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var x = it;
                rows.Add(new MRow { H = 84, Icon = Model.Db.Icon(it.Icon, Th.S(48)), IconSize = 48, Title = it.Name, Sub = it.Desc, Tap = () => Done(x) });
            }
            List.SetRows(rows);
        }
        void Done(DbItem it) { pick(it); Model.Dirty = true; M.Pop(); }
    }

    class BagPage : SavePage
    {
        MSegment seg;
        MSearch search;
        bool all;
        public BagPage(SaveModel m) : base(m) { Title = "가방"; }
        public override void Build()
        {
            base.Build();
            search = new MSearch("이름으로 찾기");
            seg = new MSegment("아이템", "무기", "방어구", "중요");
            Controls.Add(search); Controls.Add(seg);
            seg.Changed += () => OnShow();
            search.Box.TextChanged += (s, e) => OnShow();
        }
        public override void OnShow()
        {
            var db = Model.Db;
            var rows = new List<MRow>
            {
                new MRow { H = 72, Title = "안 가진 것도 보기", Toggle = () => all, Tap = () => { all = !all; OnShow(); } }
            };
            var list = Model.BagItems(seg.Selected, all, search.Box.Text);
            foreach (var it in list)
            {
                var x = it;
                rows.Add(new MRow
                {
                    H = 84, Icon = db.Icon(it.Icon, Th.S(48)), IconSize = 48, Title = it.Name, Sub = it.Desc,
                    Right = () => "×" + Model.Count(x), Dim = Model.Count(it) == 0,
                    Tap = () =>
                    {
                        var v = ValuePopup.Ask(M, x.Name, Model.Count(x), 0, Model.MaxCount);
                        if (v.HasValue) { Model.SetCount(x, (int)v.Value); Changed(); }
                    }
                });
            }
            if (list.Count == 0) rows.Add(new MRow { Title = all ? "이 칸에는 아이템이 없어요" : "가진 게 없어요. 위 스위치를 켜면 전부 보여요", Dim = true });
            List.SetRows(rows);
        }
    }

    class FlagsPage : SavePage
    {
        readonly bool sw;
        MSearch search;
        bool named = true;
        public FlagsPage(SaveModel m, bool switches) : base(m) { sw = switches; Title = switches ? "스위치" : "변수"; }
        public override void Build()
        {
            base.Build();
            search = new MSearch("이름이나 번호로 찾기");
            Controls.Add(search);
            search.Box.TextChanged += (s, e) => OnShow();
            var names = sw ? Model.Db.SwitchNames : Model.Db.VarNames;
            named = names.Any(n => n.Length > 0);
        }
        public override void OnShow()
        {
            string f = search.Box.Text.Trim();
            var rows = new List<MRow> { new MRow { H = 72, Title = "이름 붙은 것만 보기", Toggle = () => named, Tap = () => { named = !named; OnShow(); } } };
            int n = Model.FlagCount(sw);
            for (int i = 1; i < n; i++)
            {
                string name = Model.FlagName(sw, i);
                if (named && name.Length == 0) continue;
                if (f.Length > 0 && name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 && i.ToString() != f) continue;
                int k = i;
                string title = i + "   " + (name.Length > 0 ? name : "(이름 없음)");
                if (sw) rows.Add(new MRow { H = 76, Title = title, Toggle = () => Model.GetSwitch(k), Tap = () => { Model.SetSwitch(k, !Model.GetSwitch(k)); Changed(); } });
                else rows.Add(new MRow
                {
                    H = 76, Title = title,
                    Right = () => Model.VarIsNumber(k) ? Th.N(Model.GetVar(k)) : "(글자·목록)",
                    Tap = () =>
                    {
                        if (!Model.VarIsNumber(k)) { MDialog.Info(M, "이 변수는 숫자가 아니라서 여기서는 못 바꿔요"); return; }
                        var v = ValuePopup.Ask(M, title, Model.GetVar(k), -99999999, 99999999);
                        if (v.HasValue) { Model.SetVar(k, v.Value); Changed(); }
                    }
                });
            }
            if (rows.Count == 1) rows.Add(new MRow { Title = "보여줄 게 없어요", Dim = true });
            List.SetRows(rows);
        }
    }

    // 고르기 목록 (형식·검색 방식 등)
    class ChoicePage : MPage
    {
        readonly string[] opts;
        readonly int sel;
        readonly Action<int> pick;
        public ChoicePage(string title, string[] options, int selected, Action<int> onPick) { Title = title; opts = options; sel = selected; pick = onPick; }
        public override void OnShow()
        {
            List.SetRows(opts.Select((o, i) => new MRow { Title = o, Right = i == sel ? (Func<string>)(() => "✓") : null, Tap = () => { pick(i); M.Pop(); } }));
        }
    }

    class ProcPage : MPage
    {
        public ProcPage() { Title = "게임 고르기 (실행 중)"; RightText = "새로고침"; RightAction = OnShow; }
        public override void OnShow()
        {
            int me = Process.GetCurrentProcess().Id;
            var rows = new List<MRow> { new MRow { Header = true, Title = "창이 있는 프로그램" } };
            foreach (var p in Process.GetProcesses().Where(x => x.Id != me).OrderBy(x => x.ProcessName))
            {
                string t;
                try { t = p.MainWindowTitle; } catch { t = ""; }
                if (string.IsNullOrEmpty(t)) continue;
                var pp = p;
                rows.Add(new MRow { Title = t, Sub = p.ProcessName + ".exe · PID " + p.Id, Tap = () => { M.Main.AttachProc(pp); M.Pop(); } });
            }
            List.SetRows(rows);
        }
    }

    // 메모리 검색 (PC 화면의 검색 기능을 그대로 씀)
    class MemPage : MPage
    {
        readonly Timer refresh = new Timer { Interval = 500 };
        bool busy;
        public MemPage()
        {
            Title = "메모리 검색";
            refresh.Tick += (s, e) => { if (Visible) List.Invalidate(); };
        }
        public override void Build() { base.Build(); refresh.Start(); }
        protected override void Dispose(bool disposing) { refresh.Stop(); base.Dispose(disposing); }

        public override void OnShow()
        {
            var mf = M.Main;
            var rows = new List<MRow>
            {
                new MRow { Header = true, Title = "게임" },
                new MRow { H = 92, Title = mf.HasProcess ? mf.ProcText : "실행 중인 게임을 골라 주세요", Dim = !mf.HasProcess, Chevron = true, Tap = () => M.Push(new ProcPage()) },
                new MRow { Header = true, Title = "찾기" },
                new MRow { Title = "찾을 값", Right = () => mf.ValueText.Length > 0 ? mf.ValueText : "눌러서 입력", Tap = () =>
                    {
                        double cur; double.TryParse(mf.ValueText, out cur);
                        var v = NumPad.Ask(M, cur, true, true);
                        if (v.HasValue) { mf.ValueText = JVal.Fmt((double)v.Value); List.Invalidate(); }
                    } },
                new MRow { Title = "형식", Right = () => mf.KindList[mf.KindIndex], Tap = () =>
                    {
                        if (mf.Scanned) { MDialog.Info(M, "검색 중에는 형식을 바꿀 수 없어요.\n'처음부터 새 검색'을 먼저 눌러 주세요."); return; }
                        M.Push(new ChoicePage("형식", mf.KindList, mf.KindIndex, i => mf.KindIndex = i));
                    } },
                new MRow { Title = "검색 방식", Right = () => mf.CmpList[mf.CmpIndex], Tap = () => M.Push(new ChoicePage("검색 방식", mf.CmpList, mf.CmpIndex, i => mf.CmpIndex = i)) },
                new MRow { Center = true, Title = busy ? "검색 중..." : mf.Scanned ? "다음 검색" : "첫 검색", Tap = () => Scan(!mf.Scanned) },
            };
            if (mf.Scanned) rows.Add(new MRow { Center = true, Title = "처음부터 새 검색", CenterColor = Th.Red, Tap = () => { mf.NewScan(); OnShow(); } });

            rows.Add(new MRow { Header = true, Title = mf.ResultText + "  (누르면 아래 목록에 넣기)" });
            var sc = mf.Sc;
            if (sc != null && !sc.SnapMode)
                for (int i = 0; i < Math.Min(sc.Count, 100); i++)
                {
                    long ad = sc.Addr[i];
                    var k = (VK)sc.Kd[i];
                    rows.Add(new MRow
                    {
                        H = 76, Title = "주소 " + ad.ToString("X"), Sub = Val.Name(k),
                        Right = () => Val.Format(Scanner.ReadValue(mf.H, ad, k), k),
                        Tap = () => { mf.AddSavedAddr(ad, k); M.Toast("아래 '저장한 주소'에 넣었어요"); OnShow(); }
                    });
                }

            rows.Add(new MRow { Header = true, Title = "저장한 주소  (누르면 바꾸기·고정)" });
            var saved = mf.SavedList;
            for (int i = 0; i < saved.Count; i++)
            {
                var sv = saved[i];
                int idx = i;
                rows.Add(new MRow
                {
                    H = 84, Title = sv.Desc, Sub = sv.Addr.ToString("X") + (sv.Frozen ? "  ·  고정 중" : ""),
                    Right = () => Val.Format(Scanner.ReadValue(mf.H, sv.Addr, sv.Kind), sv.Kind), RightColor = sv.Frozen ? Th.Green : Th.Accent,
                    Tap = () => SavedMenu(idx)
                });
            }
            if (saved.Count == 0) rows.Add(new MRow { Title = "아직 없어요", Dim = true });
            List.SetRows(rows);
        }

        async void Scan(bool first)
        {
            var mf = M.Main;
            if (busy) return;
            if (!mf.HasProcess) { MDialog.Info(M, "먼저 실행 중인 게임을 골라 주세요"); return; }
            busy = true; OnShow();
            await mf.DoScan(first);
            busy = false;
            OnShow();
        }

        void SavedMenu(int i)
        {
            var mf = M.Main;
            var sv = mf.SavedList[i];
            int r = MDialog.Choose(M, sv.Desc, "값 바꾸기", sv.Frozen ? "고정 풀기" : "값 고정하기", "이름 바꾸기", "삭제", "취소");
            if (r == 0)
            {
                double cur = Scanner.ReadValue(mf.H, sv.Addr, sv.Kind);
                var v = ValuePopup.Ask(M, sv.Desc, double.IsNaN(cur) ? 0 : cur, Val.IsFloat(sv.Kind) ? -1e9 : -99999999, 999999999);
                if (v.HasValue) mf.SetSavedValue(i, v.Value);
            }
            else if (r == 1) mf.ToggleFreeze(i);
            else if (r == 2) { string t = Prompt.Show(M, "이름 바꾸기", "이름 (예: 골드):", sv.Desc); if (!string.IsNullOrWhiteSpace(t)) mf.RenameSaved(i, t.Trim()); }
            else if (r == 3) mf.RemoveSaved(i);
            OnShow();
        }
    }
}
