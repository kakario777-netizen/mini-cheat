using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MiniCheat
{
    // ───────── 게임 옆 팝업 모드 (쯔꾸르 런처의 「치트 모드」) ─────────
    // 포커스를 절대 가져가지 않는 창 — 게임은 계속 앞 창으로 돌아감. 숫자판·고르기도 창을 새로 띄우지 않고 팝업 안에서.
    class CheatPanel : Form, IMHost
    {
        public const string WinTitle = "MiniCheatPanel";
        readonly MainForm main;
        readonly Panel header, bottom, host;
        readonly MBtn btnBack, btnClose, btnBottom;
        readonly Label lblTitle, toast;
        readonly MSegment tabs;
        readonly Timer toastT = new Timer { Interval = 2200 };
        readonly List<Stack<MPage>> stacks = new List<Stack<MPage>> { new Stack<MPage>(), new Stack<MPage>() };
        Panel sheet;
        int tab;
        PanelSaveRoot saveRoot;

        public MainForm Main { get { return main; } }
        public IWin32Window Win { get { return this; } }
        public bool CanType { get { return false; } }

        public CheatPanel(MainForm m)
        {
            main = m;
            main.Quiet = Toast;
            Th.Init(this);
            Text = WinTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Th.Bg;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var sc = Screen.PrimaryScreen.Bounds;
            Bounds = new Rectangle(sc.Right - sc.Width * 26 / 100, sc.Top, sc.Width * 26 / 100, sc.Height);

            header = new Panel { Dock = DockStyle.Top, Height = Th.S(68), BackColor = Color.White };
            btnBack = new MBtn("‹", Color.White, Th.Accent, 26) { Dock = DockStyle.Left, Width = Th.S(64) };
            btnClose = new MBtn("✕", Color.White, Th.Sub, 18) { Dock = DockStyle.Right, Width = Th.S(68) };
            lblTitle = new Label { Dock = DockStyle.Fill, Font = Th.F(16, true), ForeColor = Th.Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            header.Controls.Add(lblTitle); header.Controls.Add(btnBack); header.Controls.Add(btnClose);
            header.Paint += (s, e) => { using (var p = new Pen(Th.Line)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
            btnBack.Click += (s, e) => Pop();
            btnClose.Click += (s, e) => HidePanel();

            tabs = new MSegment("메모리 치트", "세이브 수정");
            tabs.Changed += () => SwitchTab(tabs.Selected);

            bottom = new Panel { Dock = DockStyle.Bottom, Height = Th.S(96), BackColor = Th.Bg };
            btnBottom = new MBtn("", Th.Accent, Color.White, 17);
            bottom.Controls.Add(btnBottom);
            bottom.Resize += (s, e) => btnBottom.SetBounds(Th.S(12), Th.S(8), bottom.Width - Th.S(24), Th.S(80));
            btnBottom.Click += (s, e) => { var p = Top_; if (p != null && p.BottomAction != null) p.BottomAction(); Chrome(); };

            host = new Panel { Dock = DockStyle.Fill, BackColor = Th.Bg };
            toast = new Label { AutoSize = false, Visible = false, BackColor = Color.FromArgb(40, 44, 52), ForeColor = Color.White, Font = Th.F(14, true), TextAlign = ContentAlignment.MiddleCenter };
            toastT.Tick += (s, e) => { toastT.Stop(); toast.Visible = false; };

            Controls.Add(host); Controls.Add(bottom); Controls.Add(tabs); Controls.Add(header); Controls.Add(toast);
            Paint += (s, e) => { using (var p = new Pen(Th.Line, 2)) e.Graphics.DrawLine(p, 0, 0, 0, Height); };
        }

        // 작업 표시줄·Alt+Tab 에 안 나오고, 맨 위, 눌러도 포커스를 안 가져감 → 게임은 계속 앞 창
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8 | 0x08000000; return cp; }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATA { public IntPtr Data; public int Size; public IntPtr Ptr; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        IntPtr gameWin;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE → 포커스 안 가져감
            // 그래도 누가 이 창을 앞으로 올리면(런처가 '앞으로' 하는 경우 등) 바로 게임에 포커스를 돌려줌
            if (m.Msg == 0x06 && ((long)m.WParam & 0xFFFF) != 0 && gameWin != IntPtr.Zero && gameWin != Handle)
            {
                var g = gameWin;
                BeginInvoke((Action)(() => { try { if (IsWindow(g)) SetForegroundWindow(g); } catch { } }));
            }
            if (m.Msg == 0x4A)                                    // WM_COPYDATA: 런처가 보내는 명령
            {
                try
                {
                    var cd = (COPYDATA)Marshal.PtrToStructure(m.LParam, typeof(COPYDATA));
                    string cmd = Marshal.PtrToStringUni(cd.Ptr, cd.Size / 2);
                    BeginInvoke((Action)(() => Command(cmd)));
                    m.Result = (IntPtr)1;
                }
                catch { }
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- 명령: show / hide / pad ----------
        // show \t 게임폴더 \t 게임 pid \t x \t y \t w \t h
        public void Command(string cmd)
        {
            var p = cmd.Split('\t');
            try
            {
                switch (p[0])
                {
                    case "show":
                        {
                            int pid = p.Length > 2 ? int.Parse(p[2]) : 0;
                            if (p.Length >= 7) Bounds = new Rectangle(int.Parse(p[3]), int.Parse(p[4]), int.Parse(p[5]), int.Parse(p[6]));
                            ShowPanel(p.Length > 1 && p[1].Length > 0 ? p[1] : null, pid);
                            break;
                        }
                    case "hide": HidePanel(); break;
                    case "quit": Application.Exit(); break;
                    case "pad": Pad(p.Length > 1 ? p[1] : ""); break;
                }
            }
            catch (Exception ex) { Toast("명령을 처리하지 못했어요: " + ex.Message); }
        }

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        void ShowPanel(string dir, int pid)
        {
            try { var fg = GetForegroundWindow(); if (fg != Handle) gameWin = fg; } catch { }
            ConnectGame(dir, pid);
            if (stacks[0].Count == 0) { PushTo(0, new MemPage()); }
            if (stacks[1].Count == 0) { saveRoot = new PanelSaveRoot(this); PushTo(1, saveRoot); saveRoot.SetGame(connectedDir); }
            CloseSheet();
            ShowTab(tab);
            Show();
            try { SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); } catch { } // 맨 위, 포커스 안 뺏음
            var top = Top_;
            if (top != null) top.OnShow();
            Chrome();
        }

        public void HidePanel()
        {
            CloseSheet();
            Hide();
        }

        // 런처가 알려 준 게임 폴더·프로세스에 자동 연결
        string connectedDir;
        void ConnectGame(string dir, int pid)
        {
            Process best = null;
            try
            {
                if (dir != null)
                {
                    string d = dir.TrimEnd('\\') + "\\";
                    var cands = new List<Process>();
                    foreach (var q in Process.GetProcesses())
                    {
                        string exe = null;
                        try { exe = Native.ExePath(q.Id); } catch { }
                        if (exe != null && exe.StartsWith(d, StringComparison.OrdinalIgnoreCase)) cands.Add(q);
                    }
                    best = cands.FirstOrDefault(q => q.Id == pid)
                        ?? cands.FirstOrDefault(q => { try { return q.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
                        ?? cands.FirstOrDefault();
                }
                else if (pid > 0) best = Process.GetProcessById(pid);
            }
            catch { }
            if (best != null)
            {
                string cur = main.HasProcess ? main.ProcText : "";
                if (!main.HasProcess || !cur.Contains("PID " + best.Id) || dir != connectedDir) main.AttachProc(best);
            }
            if (dir != connectedDir)
            {
                connectedDir = dir;
                if (saveRoot != null) saveRoot.SetGame(dir);
            }
        }

        public string GameDir { get { return connectedDir; } }

        // ---------- 패드 (런처가 대신 받아서 보내 줌) ----------
        void Pad(string b)
        {
            if (b == "B")
            {
                if (sheet != null) { CloseSheet(); return; }
                if (stacks[tab].Count > 1) Pop(); else HidePanel();
            }
            else if (b == "LB" || b == "RB") { if (sheet == null) { tabs.Selected = b == "LB" ? 0 : 1; tabs.Invalidate(); SwitchTab(tabs.Selected); } }
            else if (b == "Up" || b == "Down") { var p = Top_; if (p != null && sheet == null) p.List.ScrollBy(b == "Up" ? -Th.S(240) : Th.S(240)); }
        }

        // ---------- 페이지 ----------
        MPage Top_ { get { return stacks[tab].Count > 0 ? stacks[tab].Peek() : null; } }

        void SwitchTab(int t)
        {
            if (t == tab) return;
            CloseSheet();
            tab = t;
            ShowTab(t);
            var p = Top_;
            if (p != null) p.OnShow();
            Chrome();
        }

        void ShowTab(int t)
        {
            for (int i = 0; i < stacks.Count; i++)
                foreach (var p in stacks[i]) p.Visible = i == t && p == stacks[i].Peek();
            tabs.Selected = t;
            tabs.Invalidate();
        }

        void PushTo(int t, MPage p)
        {
            if (stacks[t].Count > 0) stacks[t].Peek().Visible = false;
            p.M = this;
            p.Dock = DockStyle.Fill;
            p.Visible = t == tab;
            host.Controls.Add(p);
            stacks[t].Push(p);
            p.Build();
            if (t == tab) p.OnShow();
        }

        public void Push(MPage p) { PushTo(tab, p); Chrome(); }

        public void Pop()
        {
            if (stacks[tab].Count <= 1) return;
            stacks[tab].Peek().TryLeave(() =>
            {
                if (stacks[tab].Count <= 1) return;
                var p = stacks[tab].Pop();
                host.Controls.Remove(p);
                p.Dispose();
                var t = stacks[tab].Peek();
                t.Visible = true;
                t.OnShow();
                Chrome();
            });
        }

        public void Chrome()
        {
            var p = Top_;
            if (p == null) return;
            lblTitle.Text = p.Title;
            btnBack.Visible = stacks[tab].Count > 1;
            lblTitle.Padding = new Padding(stacks[tab].Count > 1 ? 0 : Th.S(18), 0, 0, 0);
            bottom.Visible = p.BottomText != null;
            btnBottom.Text = p.BottomText ?? "";
            bool en = p.BottomEnabled == null || p.BottomEnabled();
            btnBottom.Enabled = en;
            btnBottom.BackColor = en ? Th.Accent : Color.FromArgb(190, 198, 214);
        }

        public void Toast(string text)
        {
            int w = Width - Th.S(32), h = Th.S(84);
            toast.Text = text;
            toast.SetBounds(Th.S(16), ClientSize.Height - h - Th.S(110), w, h);
            toast.Visible = true;
            toast.BringToFront();
            toastT.Stop(); toastT.Start();
        }

        public void SaveModelNow(SaveModel m)
        {
            if (!m.Dirty) return;
            Action doSave = () =>
            {
                try { m.Save(); Toast("저장했어요! 게임에서 이 세이브를 불러오세요"); }
                catch (Exception ex) { this.Info("저장하지 못했어요\n" + ex.Message); }
                Chrome();
                var p = Top_; if (p != null) p.OnShow();
            };
            if (m.ChangedOnDisk) Choose("그 사이 게임이 이 세이브를 다시 저장했어요.\n그래도 덮어쓸까요? (원래 파일은 백업돼요)", new[] { "덮어쓰기", "취소" }, r => { if (r == 0) doSave(); });
            else doSave();
        }

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
                if (Translator.CountMissing(texts) > 0) await Task.Run(() => Translator.Translate(texts, null));
                g.ApplyLang(true);
                g.SaveTrToGame();
                done();
            }
            catch { g.ApplyLang(true); done(); }
        }

        // ---------- 팝업 안 시트 (창을 새로 띄우지 않음) ----------
        void ShowSheet(Control card)
        {
            CloseSheet();
            sheet = new Panel { BackColor = Color.FromArgb(96, 100, 110), Bounds = ClientRectangle };
            card.Left = Th.S(12);
            card.Width = ClientSize.Width - Th.S(24);
            card.Top = Math.Max(Th.S(12), ClientSize.Height - card.Height - Th.S(12));
            sheet.Controls.Add(card);
            sheet.MouseUp += (s, e) => { if (!card.Bounds.Contains(e.Location)) CloseSheet(); };
            Controls.Add(sheet);
            sheet.BringToFront();
        }

        void CloseSheet()
        {
            if (sheet == null) return;
            var s = sheet;
            sheet = null;
            Controls.Remove(s);
            s.Dispose();
        }

        Panel Card(int h) { return new Panel { BackColor = Color.White, Height = h }; }

        public void Choose(string message, string[] options, Action<int> pick)
        {
            int w = ClientSize.Width - Th.S(24), pad = Th.S(16), y = pad;
            var f = Th.F(15, true);
            int th = TextRenderer.MeasureText(message, f, new Size(w - pad * 2, 2000), TextFormatFlags.WordBreak).Height + Th.S(12);
            var card = Card(0);
            card.Controls.Add(new Label { Text = message, Font = f, ForeColor = Th.Text, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(pad, y, w - pad * 2, th) });
            y += th + Th.S(8);
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                bool cancel = options[i] == "취소";
                var b = new MBtn(options[i], i == 0 && !cancel ? Th.Accent : Th.Bg, i == 0 && !cancel ? Color.White : Th.Text, 15) { Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(72)) };
                b.Click += (s, e) => { CloseSheet(); if (!cancel) pick(idx); };
                card.Controls.Add(b);
                y += Th.S(80);
            }
            card.Height = y + pad - Th.S(8);
            ShowSheet(card);
        }

        public void AskValue(string title, double cur, double min, double max, Action<double> ok)
        {
            max = Math.Max(min, max);
            double val = Math.Max(min, Math.Min(max, cur));
            int w = ClientSize.Width - Th.S(24), pad = Th.S(14), gap = Th.S(8), y = pad;
            var card = Card(0);
            card.Controls.Add(new Label { Text = title, Font = Th.F(14, true), ForeColor = Th.Sub, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(34)), AutoEllipsis = true });
            y += Th.S(38);
            var lblVal = new Label { Font = Th.F(30, true), ForeColor = Th.Text, TextAlign = ContentAlignment.MiddleCenter, BackColor = Th.Bg, Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(76)), Cursor = Cursors.Hand };
            Action<double> set = v => { val = Math.Max(min, Math.Min(max, Math.Round(v))); lblVal.Text = Th.N(val); };
            set(val);
            lblVal.Click += (s, e) => AskNumber(val, min < 0, v => AskValue(title, v, min, max, ok));
            card.Controls.Add(lblVal);
            y += Th.S(80);
            card.Controls.Add(new Label { Text = "숫자를 누르면 직접 입력 · 최대 " + Th.N(max), Font = Th.F(11), ForeColor = Th.Sub, TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(28)) });
            y += Th.S(34);

            // 단위: 최대값/5 이하인 1·10·100… 중 큰 쪽 6개, 한 줄에 3개씩 (+ 줄들, − 줄들)
            var steps = new List<double>();
            for (double st = 1; st <= Math.Max(1, (max - min) / 5); st *= 10) steps.Add(st);
            if (steps.Count > 6) steps = steps.Skip(steps.Count - 6).ToList();
            int cols = Math.Min(3, steps.Count), bw = (w - pad * 2 - gap * (cols - 1)) / cols, bh = Th.S(68);
            foreach (int sign in new[] { 1, -1 })
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    double st = steps[i];
                    int c = i % cols;
                    if (i > 0 && c == 0) y += bh + gap;
                    var b = new MBtn((sign > 0 ? "+" : "−") + Unit(st), sign > 0 ? Color.FromArgb(225, 243, 234) : Color.FromArgb(252, 231, 231), sign > 0 ? Th.Green : Th.Red, 16)
                    { Bounds = new Rectangle(pad + c * (bw + gap), y, bw, bh) };
                    b.Click += (s, e) => set(val + sign * st);
                    card.Controls.Add(b);
                }
                y += bh + gap;
            }
            int half = (w - pad * 2 - gap) / 2;
            var b0 = new MBtn(min < 0 ? "0" : "최소", Th.Bg, Th.Text, 15) { Bounds = new Rectangle(pad, y, half, Th.S(64)) };
            var bm = new MBtn("최대", Th.Bg, Th.Text, 15) { Bounds = new Rectangle(pad + half + gap, y, half, Th.S(64)) };
            b0.Click += (s, e) => set(min < 0 ? 0 : min);
            bm.Click += (s, e) => set(max);
            card.Controls.Add(b0); card.Controls.Add(bm);
            y += Th.S(64) + gap * 2;
            var no = new MBtn("취소", Th.Bg, Th.Text, 16) { Bounds = new Rectangle(pad, y, half, Th.S(76)) };
            var okb = new MBtn("확인", Th.Accent, Color.White, 16) { Bounds = new Rectangle(pad + half + gap, y, half, Th.S(76)) };
            no.Click += (s, e) => CloseSheet();
            okb.Click += (s, e) => { CloseSheet(); ok(val); };
            card.Controls.Add(no); card.Controls.Add(okb);
            card.Height = y + Th.S(76) + pad;
            ShowSheet(card);
        }

        static string Unit(double s)
        {
            if (s >= 1e8) return (s / 1e8) + "억";
            if (s >= 1e4) return (s / 1e4) + "만";
            if (s >= 1e3) return (s / 1e3) + "천";
            return s.ToString();
        }

        public void AskNumber(double cur, bool neg, Action<double> ok)
        {
            string text = "";
            int w = ClientSize.Width - Th.S(24), pad = Th.S(14), gap = Th.S(8), y = pad;
            var card = Card(0);
            var disp = new Label { Font = Th.F(28, true), ForeColor = Th.Text, TextAlign = ContentAlignment.MiddleRight, BackColor = Th.Bg, Bounds = new Rectangle(pad, y, w - pad * 2, Th.S(76)), Padding = new Padding(0, 0, Th.S(10), 0) };
            Action upd = () => disp.Text = text.Length == 0 ? Th.N(cur) : text;
            upd();
            card.Controls.Add(disp);
            y += Th.S(84);
            string[] keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", neg ? "±" : "", "0", "←" };
            int bw = (w - pad * 2 - gap * 2) / 3, bh = Th.S(70);
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                if (k.Length == 0) continue;
                var b = new MBtn(k, Th.Bg, Th.Text, 20) { Bounds = new Rectangle(pad + i % 3 * (bw + gap), y + i / 3 * (bh + gap), bw, bh) };
                b.Click += (s, e) =>
                {
                    if (k == "←") text = text.Length > 0 ? text.Substring(0, text.Length - 1) : "";
                    else if (k == "±") text = text.StartsWith("-") ? text.Substring(1) : "-" + text;
                    else if (text.Replace("-", "").Length < 11) text = text == "0" ? k : text + k;
                    upd();
                };
                card.Controls.Add(b);
            }
            y += 4 * (bh + gap) + gap;
            int half = (w - pad * 2 - gap) / 2;
            var no = new MBtn("취소", Th.Bg, Th.Text, 16) { Bounds = new Rectangle(pad, y, half, Th.S(76)) };
            var okb = new MBtn("확인", Th.Accent, Color.White, 16) { Bounds = new Rectangle(pad + half + gap, y, half, Th.S(76)) };
            no.Click += (s, e) => CloseSheet();
            okb.Click += (s, e) =>
            {
                CloseSheet();
                double v;
                if (text.Length == 0 || text == "-") v = cur;
                else double.TryParse(text, out v);
                ok(v);
            };
            card.Controls.Add(no); card.Controls.Add(okb);
            card.Height = y + Th.S(76) + pad;
            ShowSheet(card);
        }

        // ---------- 하나만 켜기 (이미 켜져 있으면 그 창에 명령만 보냄) ----------
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref COPYDATA l);

        public static bool SendToRunning(string cmd)
        {
            IntPtr h = FindWindow(null, WinTitle);
            if (h == IntPtr.Zero) return false;
            IntPtr mem = Marshal.StringToHGlobalUni(cmd);
            try
            {
                var cd = new COPYDATA { Data = IntPtr.Zero, Size = (cmd.Length + 1) * 2, Ptr = mem };
                SendMessage(h, 0x4A, IntPtr.Zero, ref cd);
            }
            finally { Marshal.FreeHGlobal(mem); }
            return true;
        }

        // --panel --dir "폴더" --pid 123 --rect x,y,w,h → "show\t..." 명령
        public static string CommandFromArgs(string[] args)
        {
            string dir = "", pid = "0", rect = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--dir") dir = args[i + 1];
                else if (args[i] == "--pid") pid = args[i + 1];
                else if (args[i] == "--rect") rect = args[i + 1];
            }
            string cmd = "show\t" + dir + "\t" + pid;
            if (rect != null) { var r = rect.Split(','); if (r.Length == 4) cmd += "\t" + string.Join("\t", r); }
            return cmd;
        }
    }

    // 팝업 「세이브 수정」 첫 페이지: 지금 게임의 가장 최근 세이브를 바로 엶
    class PanelSaveRoot : SavePage
    {
        readonly CheatPanel panel;
        GameData db;
        string dir;
        DateTime openedTime;

        public PanelSaveRoot(CheatPanel p) : base(null)
        {
            panel = p;
            Title = "세이브 수정";
            BottomEnabled = () => Model != null && Model.Dirty;
            BottomAction = () => { if (Model != null) M.SaveModelNow(Model); OnShow(); };
        }

        public void SetGame(string d)
        {
            dir = d;
            db = null; Model = null;
            if (d == null || GameData.DetectKind(d) == RpgKind.Unknown) return;
            try
            {
                db = GameData.Load(d);
                Backup.RememberGame(d);
                M.Translate(db, () => { if (!IsDisposed) { Title = db.Title; if (Visible) OnShow(); M.Chrome(); } });
            }
            catch { db = null; }
        }

        void OpenSlot(SaveSlot s, bool announce)
        {
            try
            {
                Model = SaveModel.Open(db, s);
                openedTime = File.GetLastWriteTimeUtc(s.Path);
                if (announce) M.Toast(s.Label + " 을(를) 열었어요");
            }
            catch (Exception ex) { M.Info("세이브를 읽지 못했어요\n" + ex.Message); }
        }

        public override void OnShow()
        {
            var rows = new List<MRow>();
            if (db == null)
            {
                Title = "세이브 수정";
                rows.Add(new MRow { H = 120, Title = "쯔꾸르 게임이 아니에요", Sub = "쯔꾸르 게임을 하는 중에 열면 세이브가 바로 나와요", Dim = true });
                List.SetRows(rows);
                return;
            }
            Title = db.Title;
            if (!db.CanEdit)
            {
                rows.Add(new MRow { H = 120, Title = db.KindName + " 세이브는 아직 못 고쳐요", Dim = true });
                List.SetRows(rows);
                return;
            }
            var slots = db.FindSaves();
            var newest = slots.OrderByDescending(x => File.GetLastWriteTimeUtc(x.Path)).FirstOrDefault();
            // 바꾼 게 없으면 게임이 새로 저장한 세이브로 자동으로 바꿔 엶
            if (newest != null && (Model == null || (!Model.Dirty && (newest.Path != Model.Slot.Path || File.GetLastWriteTimeUtc(newest.Path) != openedTime))))
                OpenSlot(newest, Model != null);
            if (Model == null)
            {
                rows.Add(new MRow { H = 120, Title = "아직 세이브가 없어요", Sub = "게임에서 한 번 저장하면 여기 바로 나와요", Dim = true });
                List.SetRows(rows);
                return;
            }
            var m = Model;
            rows.Add(new MRow
            {
                H = 92, Title = m.Slot.Label + "  ·  " + File.GetLastWriteTime(m.Slot.Path).ToString("HH:mm") + " 저장",
                Sub = "다른 세이브 고르기", Chevron = true,
                Tap = () =>
                {
                    var labels = slots.Select(x => x.Label + "  ·  " + x.When).ToArray();
                    int sel = slots.FindIndex(x => x.Path == m.Slot.Path);
                    M.Push(new ChoicePage("세이브 고르기", labels, sel, i =>
                    {
                        if (Model != null && Model.Dirty)
                            M.Choose("바꾼 내용을 저장하지 않았어요", new[] { "저장 안 하고 바꾸기", "취소" }, r => { if (r == 0) { OpenSlot(slots[i], false); OnShow(); } });
                        else OpenSlot(slots[i], false);
                    }));
                }
            });
            rows.Add(new MRow { Header = true, Title = "고치기  (저장한 뒤 게임에서 불러오기)" });
            rows.Add(new MRow { H = 88, Title = "돈", Right = () => Th.N(m.Gold) + " " + db.Currency, Tap = () => M.AskValue("돈", m.Gold, 0, m.MaxGold, v => { m.Gold = v; Changed(); }) });
            rows.Add(new MRow { Title = "가방 (아이템 · 장비)", Chevron = true, Tap = () => M.Push(new BagPage(m)) });
            rows.Add(new MRow { Title = "파티 (캐릭터)", Right = () => m.PartyIds().Count + "명", Chevron = true, Tap = () => M.Push(new PartyPage(m)) });
            rows.Add(new MRow { Title = "스위치", Chevron = true, Tap = () => M.Push(new FlagsPage(m, true)) });
            rows.Add(new MRow { Title = "변수", Chevron = true, Tap = () => M.Push(new FlagsPage(m, false)) });
            rows.Add(new MRow { Title = "플레이 시간", Right = () => { double s = m.PlaySeconds; return (long)(s / 3600) + "시간 " + (long)(s / 60 % 60) + "분"; },
                Tap = () => { double s = m.PlaySeconds; M.AskValue("플레이 시간 (시간)", Math.Floor(s / 3600), 0, 9999, v => { m.PlaySeconds = v * 3600 + s % 3600; Changed(); }); } });
            rows.Add(new MRow { Header = true, Title = "기타" });
            rows.Add(new MRow { Title = "백업 · 복원", Chevron = true, Tap = () => M.Push(new BackupsPage(db, slots)) });
            List.SetRows(rows);
        }

        public override void TryLeave(Action leave) { leave(); }
    }
}
