using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MiniCheat
{
    class Saved
    {
        public long Addr;
        public VK Kind;
        public string Desc = "설명 없음";
        public bool Frozen;
        public double FrozenVal;
    }

    class MainForm : Form
    {
        // 형식 목록 (RpgDetect 의 번호와 맞춤)
        static readonly string[] KindNames = {
            "4바이트", "2바이트", "1바이트", "8바이트", "소수 (float)", "소수 (double)",
            "쯔꾸르 2000/2003", "쯔꾸르 XP/VX/VX Ace", "쯔꾸르 MV/MZ (자동)" };
        static readonly VK[][] KindSets = {
            new[]{VK.I4}, new[]{VK.I2}, new[]{VK.I1}, new[]{VK.I8}, new[]{VK.F4}, new[]{VK.F8},
            new[]{VK.I4}, new[]{VK.Rgss}, new[]{VK.I4, VK.Smi, VK.F8} };

        static readonly string[] FirstCmps = { "정확한 값", "처음 값 모름" };
        static readonly Cmp[] FirstVals = { Cmp.Exact, Cmp.Unknown };
        static readonly string[] NextCmps = { "정확한 값", "커진 값", "작아진 값", "변한 값", "안 변한 값" };
        static readonly Cmp[] NextVals = { Cmp.Exact, Cmp.Bigger, Cmp.Smaller, Cmp.Changed, Cmp.Same };

        Process proc;
        IntPtr h = IntPtr.Zero;
        Scanner sc;
        bool scanned, busy, suppressCheck;
        readonly List<Saved> saved = new List<Saved>();

        Label lblProc, lblCount, lblStatus;
        TextBox txtVal;
        ComboBox cmbKind, cmbCmp;
        Button btnFirst, btnNext, btnNew, btnAdd;
        ProgressBar bar;
        ListView lvRes, lvSaved;
        SaveEditor saveEd;
        Button btnGame;
        TabControl mainTabs;
        readonly Timer tRefresh = new Timer { Interval = 500 };
        readonly Timer tFreeze = new Timer { Interval = 100 };

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("맑은 고딕", 9F);
            Text = "미니 치트 v" + Updater.Current;
            ClientSize = new Size(1100, 700);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ── 위: 프로세스 선택 ──
            var top = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 7, 8, 7) };
            lblProc = new Label { Text = "선택된 프로그램 없음", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            var btnProc = new Button { Text = "프로세스 선택", Dock = DockStyle.Left, Width = 110 };
            var btnUpd = new Button { Text = "업데이트 확인", Dock = DockStyle.Right, Width = 100 };
            btnGame = new Button { Text = "게임기 모드", Dock = DockStyle.Right, Width = 100 };
            top.Controls.Add(lblProc); top.Controls.Add(btnProc); top.Controls.Add(btnGame); top.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 }); top.Controls.Add(btnUpd);
            btnGame.Click += (s, e) => SetGameMode(!Touch.On);
            btnProc.Click += (s, e) => PickProcess();
            btnUpd.Click += (s, e) => CheckUpdate(true);

            // ── 오른쪽: 검색 조작 ──
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 250, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8, 4, 8, 4), AutoScroll = true };
            txtVal = new TextBox { Width = 225 };
            cmbKind = new ComboBox { Width = 225, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKind.Items.AddRange(KindNames);
            cmbKind.SelectedIndex = 0;
            cmbCmp = new ComboBox { Width = 225, DropDownStyle = ComboBoxStyle.DropDownList };
            btnFirst = new Button { Text = "첫 검색", Width = 225, Height = 30, Margin = new Padding(3, 10, 3, 3) };
            btnNext = new Button { Text = "다음 검색", Width = 225, Height = 30 };
            btnNew = new Button { Text = "새 검색 (처음부터)", Width = 225, Height = 30 };
            bar = new ProgressBar { Width = 225, Height = 12, Margin = new Padding(3, 6, 3, 6) };
            btnAdd = new Button { Text = "선택한 결과 → 아래 목록에 추가", Width = 225, Height = 30 };
            var hint = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(225, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(3, 12, 3, 3),
                Text = "찾는 법\n" +
                       "1. 지금 값(예: 골드 500)을 넣고 첫 검색\n" +
                       "2. 게임에서 값을 바꾼 뒤 새 값으로 다음 검색\n" +
                       "3. 몇 개 남으면 더블클릭해서 아래 목록에 넣고 값을 바꾸기\n\n" +
                       "숫자가 안 보이는 값(체력 바 등)은 '처음 값 모름'으로 시작해서, 줄면 '작아진 값', 늘면 '커진 값'으로 좁혀요.\n\n" +
                       "쯔꾸르 게임은 고르면 형식이 자동으로 맞춰져요. 게임 화면에 보이는 숫자 그대로 넣으면 돼요."
            };
            right.Controls.AddRange(new Control[] {
                new Label { Text = "값", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, txtVal,
                new Label { Text = "형식", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, cmbKind,
                new Label { Text = "검색 방식", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, cmbCmp,
                btnFirst, btnNext, btnNew, bar, btnAdd, hint });

            // ── 왼쪽: 검색 결과 ──
            var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
            lblCount = new Label { Text = "결과 없음", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            lvRes = new DBListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, VirtualMode = true };
            lvRes.Columns.Add("주소", 130);
            lvRes.Columns.Add("형식", 100);
            lvRes.Columns.Add("현재 값", 110);
            lvRes.Columns.Add("이전 값", 110);
            leftPanel.Controls.Add(lvRes); leftPanel.Controls.Add(lblCount);

            var center = new Panel { Dock = DockStyle.Fill };
            center.Controls.Add(leftPanel); center.Controls.Add(right);

            // ── 아래: 저장한 주소 ──
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 210, Padding = new Padding(8, 0, 8, 0) };
            var lblSaved = new Label { Text = "저장한 주소  —  체크: 값 고정 · 더블클릭: 값 바꾸기 · 설명 칸 더블클릭: 설명 바꾸기 · Delete: 삭제", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            lvSaved = new DBListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, CheckBoxes = true };
            lvSaved.Columns.Add("고정 / 설명", 240);
            lvSaved.Columns.Add("주소", 130);
            lvSaved.Columns.Add("형식", 120);
            lvSaved.Columns.Add("값", 150);
            bottom.Controls.Add(lvSaved); bottom.Controls.Add(lblSaved);
            var split = new Splitter { Dock = DockStyle.Bottom, Height = 6 };

            lblStatus = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), ForeColor = Color.DimGray, Text = "준비됨" };

            var pageMem = new TabPage("메모리 검색");
            pageMem.Controls.Add(center); pageMem.Controls.Add(split); pageMem.Controls.Add(bottom);
            var pageSave = new TabPage("세이브 수정");
            saveEd = new SaveEditor { Dock = DockStyle.Fill };
            pageSave.Controls.Add(saveEd);
            mainTabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
            mainTabs.TabPages.Add(pageMem); mainTabs.TabPages.Add(pageSave);

            Controls.Add(mainTabs); Controls.Add(lblStatus); Controls.Add(top);

            // ── 메뉴 ──
            var mRes = new ContextMenuStrip();
            mRes.Items.Add("아래 목록에 추가", null, (s, e) => AddSelected());
            mRes.Items.Add("선택한 결과 값 한꺼번에 바꾸기", null, (s, e) => EditResults());
            lvRes.ContextMenuStrip = mRes;
            var mSav = new ContextMenuStrip();
            mSav.Items.Add("값 바꾸기", null, (s, e) => { if (lvSaved.SelectedIndices.Count > 0) EditValue(lvSaved.SelectedIndices[0]); });
            mSav.Items.Add("설명 바꾸기", null, (s, e) => { if (lvSaved.SelectedIndices.Count > 0) EditDesc(lvSaved.SelectedIndices[0]); });
            mSav.Items.Add("삭제", null, (s, e) => DeleteSaved());
            lvSaved.ContextMenuStrip = mSav;

            // ── 이벤트 ──
            btnFirst.Click += (s, e) => Scan(true);
            btnNext.Click += (s, e) => Scan(false);
            btnNew.Click += (s, e) => ResetScan();
            btnAdd.Click += (s, e) => AddSelected();
            txtVal.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                Scan(!scanned);
            };
            lvRes.RetrieveVirtualItem += OnRetrieve;
            lvRes.DoubleClick += (s, e) => AddSelected();
            lvSaved.DoubleClick += OnSavedDouble;
            lvSaved.ItemChecked += OnSavedChecked;
            lvSaved.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteSaved(); };
            tRefresh.Tick += (s, e) => RefreshValues();
            tFreeze.Tick += (s, e) => DoFreeze();
            tRefresh.Start(); tFreeze.Start();

            SetCmpItems(false);
            UpdateButtons();
            ResumeLayout(false);
            PerformLayout();
            Touch.Hook(this);
            Touch.HookNumberBox(txtVal);
        }

        // 게임기 모드: 휴대폰 앱 같은 큰 화면(MobileForm)으로 바꿈
        MobileForm mobile;
        internal void SetGameMode(bool on)
        {
            Touch.SaveSetting(on);
            if (on)
            {
                if (mobile != null) return;
                mobile = new MobileForm(this);
                mobile.FormClosed += (s, e) =>
                {
                    bool back = mobile.BackToPc;
                    mobile = null;
                    if (back) { Show(); Activate(); } else Close();
                };
                Hide();
                mobile.Show();
            }
            else if (mobile != null) { mobile.BackToPc = true; mobile.Close(); }
        }

        // ── 게임기 모드 화면이 쓰는 것들 ──
        internal void CheckUpdatePublic() { CheckUpdate(true); }

        // 팝업 모드: 윈도우 메시지 상자를 띄우지 않고(포커스를 뺏으니까) 이 함수로 알림
        internal Action<string> Quiet;
        void Msg(string text)
        {
            if (Quiet != null) Quiet(text);
            else MessageBox.Show(this, text, "미니 치트");
        }
        internal bool HasProcess { get { return h != IntPtr.Zero; } }
        internal IntPtr H { get { return h; } }
        internal string ProcText { get { return lblProc.Text; } }
        internal string RunningGameDir
        {
            get
            {
                if (proc == null) return null;
                string exe = Native.ExePath(proc.Id);
                if (exe == null) return null;
                string d = Path.GetDirectoryName(exe);
                return GameData.DetectKind(d) == RpgKind.Unknown ? null : d;
            }
        }
        internal void AttachProc(Process p) { Attach(p); }
        internal string ValueText { get { return txtVal.Text; } set { txtVal.Text = value; } }
        internal string[] KindList { get { return KindNames; } }
        internal int KindIndex { get { return cmbKind.SelectedIndex; } set { if (!scanned) cmbKind.SelectedIndex = value; } }
        internal string[] CmpList { get { return scanned ? NextCmps : FirstCmps; } }
        internal int CmpIndex { get { return Math.Max(0, cmbCmp.SelectedIndex); } set { cmbCmp.SelectedIndex = value; } }
        internal bool Scanned { get { return scanned; } }
        internal Scanner Sc { get { return busy ? null : sc; } }
        internal string ResultText { get { return lblCount.Text; } }
        internal void NewScan() { ResetScan(); }
        internal Task DoScan(bool first) { return Scan(first); }
        internal List<Saved> SavedList { get { return saved; } }
        internal void AddSavedAddr(long a, VK k) { AddSaved(a, k); }
        internal void SetSavedValue(int i, double v)
        {
            var sv = saved[i];
            Scanner.WriteValue(h, sv.Addr, sv.Kind, v);
            if (sv.Frozen) sv.FrozenVal = v;
        }
        internal void ToggleFreeze(int i) { lvSaved.Items[i].Checked = !lvSaved.Items[i].Checked; }
        internal void RenameSaved(int i, string t) { saved[i].Desc = t; lvSaved.Items[i].Text = t; }
        internal void RemoveSaved(int i) { saved.RemoveAt(i); lvSaved.Items.RemoveAt(i); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Touch.LoadSetting()) SetGameMode(true);
            CheckUpdate(false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!saveEd.AskClose()) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Detach();
            base.OnFormClosed(e);
        }

        static string Hex(long a) { return a.ToString(a > 0xFFFFFFFFL ? "X12" : "X8"); }

        // ───────── 프로세스 ─────────
        void PickProcess()
        {
            if (busy) return;
            using (var f = new ProcessForm())
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Selected == null) return;
                Attach(f.Selected);
            }
        }

        void Attach(Process p)
        {
            string exe = Native.ExePath(p.Id), rpg;
            int k = RpgDetect.Detect(exe, out rpg);
            string extra = "";
            if (k == RpgDetect.KMv)
            {
                // MV/MZ 는 같은 이름 프로세스가 여러 개 — 게임 데이터는 메모리를 가장 많이 쓰는 쪽에 있음
                var best = PickBiggest(exe);
                if (best != null && best.Id != p.Id) { p = best; extra = "  · 게임 데이터 프로세스 자동 선택"; }
            }
            IntPtr nh = Native.OpenProcess(Native.AccessAll, false, p.Id);
            if (nh == IntPtr.Zero)
            {
                Msg("이 프로그램에 접근하지 못했어요.\n미니 치트를 마우스 오른쪽 버튼 → '관리자 권한으로 실행'으로 켜 보세요.");
                return;
            }
            Detach();
            proc = p; h = nh;
            lblProc.Text = p.ProcessName + ".exe  (PID " + p.Id + ")" + (rpg != null ? "  —  " + rpg + " 감지" : "") + extra;
            if (k >= 0) cmbKind.SelectedIndex = k;
            ResetScan();
            if (k >= 0) saveEd.OfferGame(exe); // 세이브 수정 탭에도 같은 게임
        }

        static Process PickBiggest(string exe)
        {
            if (exe == null) return null;
            Process best = null; long bestMem = -1;
            foreach (var q in Process.GetProcesses())
            {
                try
                {
                    if (!string.Equals(Native.ExePath(q.Id), exe, StringComparison.OrdinalIgnoreCase)) continue;
                    long m = q.PrivateMemorySize64;
                    if (m > bestMem) { bestMem = m; best = q; }
                }
                catch { }
            }
            return best;
        }

        void Detach()
        {
            if (h != IntPtr.Zero) Native.CloseHandle(h);
            h = IntPtr.Zero; proc = null; sc = null;
            saved.Clear();
            suppressCheck = true; lvSaved.Items.Clear(); suppressCheck = false;
        }

        bool Alive()
        {
            try { if (proc != null && !proc.HasExited) return true; } catch { }
            return false;
        }

        // ───────── 검색 ─────────
        void SetCmpItems(bool next)
        {
            cmbCmp.Items.Clear();
            cmbCmp.Items.AddRange(next ? NextCmps : FirstCmps);
            cmbCmp.SelectedIndex = 0;
        }

        void UpdateButtons()
        {
            bool ok = h != IntPtr.Zero && !busy;
            btnFirst.Enabled = ok && !scanned;
            btnNext.Enabled = ok && scanned;
            btnNew.Enabled = ok && scanned;
            btnAdd.Enabled = !busy;
            cmbKind.Enabled = !scanned && !busy;
            cmbCmp.Enabled = !busy;
        }

        void ResetScan()
        {
            sc = null; scanned = false;
            lvRes.SelectedIndices.Clear();
            lvRes.VirtualListSize = 0;
            lblCount.Text = "결과 없음";
            bar.Value = 0;
            SetCmpItems(false);
            UpdateButtons();
        }

        static bool ParseNum(string s, out double v)
        {
            s = (s ?? "").Trim().Replace(",", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        async Task Scan(bool first)
        {
            if (busy || h == IntPtr.Zero) return;
            if (!Alive()) { Msg("게임이 꺼진 것 같아요. 프로세스를 다시 골라 주세요."); return; }
            Cmp c = (scanned ? NextVals : FirstVals)[Math.Max(0, cmbCmp.SelectedIndex)];
            double t = 0;
            if (c == Cmp.Exact && !ParseNum(txtVal.Text, out t))
            {
                Msg("찾을 값을 숫자로 넣어 주세요.");
                txtVal.Focus();
                return;
            }
            if (first) sc = new Scanner(h, KindSets[cmbKind.SelectedIndex]);
            var s = sc;
            s.Tol = txtVal.Text.Contains(".") ? 0.001 : 0.5;
            s.Progress = p => { try { BeginInvoke((Action)(() => bar.Value = Math.Max(0, Math.Min(100, p)))); } catch { } };

            busy = true; UpdateButtons();
            bar.Value = 0; lblStatus.Text = "검색 중...";
            var sw = Stopwatch.StartNew();
            bool ok = true;
            try
            {
                await Task.Run(() => { if (first) s.First(c, t); else s.Next(c, t); });
            }
            catch (OutOfMemoryException)
            {
                ok = false;
                Msg("메모리가 부족해요. '처음 값 모름' 대신 정확한 값으로 검색해 보세요.");
            }
            catch (Exception ex)
            {
                ok = false;
                Msg("검색 중 문제가 생겼어요: " + ex.Message);
            }
            busy = false;
            if (ok)
            {
                if (first) { scanned = true; SetCmpItems(true); }
                bar.Value = 100;
                lblStatus.Text = "검색 끝 (" + sw.Elapsed.TotalSeconds.ToString("0.0") + "초)";
            }
            else
            {
                if (first) sc = null;
                lblStatus.Text = "검색 실패";
            }
            ShowResults();
            UpdateButtons();
        }

        void ShowResults()
        {
            lvRes.SelectedIndices.Clear();
            if (sc == null) { lvRes.VirtualListSize = 0; lblCount.Text = "결과 없음"; return; }
            if (sc.SnapMode)
            {
                lvRes.VirtualListSize = 0;
                lblCount.Text = "후보 " + sc.SnapCount.ToString("N0") + "개 — 많아서 목록은 숨겼어요. 게임에서 값을 바꾼 뒤 다음 검색하세요.";
            }
            else
            {
                lvRes.VirtualListSize = sc.Count;
                lblCount.Text = "결과 " + sc.Count.ToString("N0") + "개" +
                    (sc.Truncated ? "  (너무 많아서 일부만 담았어요. 다음 검색으로 좁혀 주세요)" : "");
            }
            lvRes.Invalidate();
        }

        void OnRetrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            var s = sc;
            int i = e.ItemIndex;
            if (s == null || busy || s.SnapMode || i >= s.Count)
            {
                e.Item = new ListViewItem(new[] { "", "", "", "" });
                return;
            }
            long a = s.Addr[i];
            var k = (VK)s.Kd[i];
            e.Item = new ListViewItem(new[] { Hex(a), Val.Name(k), Val.Format(Scanner.ReadValue(h, a, k), k), Val.Format(s.Prev[i], k) });
        }

        // ───────── 저장 목록 ─────────
        void AddSelected()
        {
            var s = sc;
            if (s == null || busy || s.SnapMode) return;
            int n = 0;
            foreach (int i in lvRes.SelectedIndices)
            {
                if (i >= s.Count) continue;
                AddSaved(s.Addr[i], (VK)s.Kd[i]);
                if (++n >= 200) break;
            }
        }

        void AddSaved(long a, VK k)
        {
            var sv = new Saved { Addr = a, Kind = k };
            saved.Add(sv);
            suppressCheck = true;
            lvSaved.Items.Add(new ListViewItem(new[] { sv.Desc, Hex(a), Val.Name(k), Val.Format(Scanner.ReadValue(h, a, k), k) }));
            suppressCheck = false;
        }

        void OnSavedChecked(object sender, ItemCheckedEventArgs e)
        {
            if (suppressCheck) return;
            int i = e.Item.Index;
            if (i < 0 || i >= saved.Count) return;
            var sv = saved[i];
            sv.Frozen = e.Item.Checked;
            if (sv.Frozen)
            {
                sv.FrozenVal = Scanner.ReadValue(h, sv.Addr, sv.Kind);
                if (double.IsNaN(sv.FrozenVal)) { sv.Frozen = false; suppressCheck = true; e.Item.Checked = false; suppressCheck = false; }
            }
        }

        void OnSavedDouble(object sender, EventArgs e)
        {
            var ht = lvSaved.HitTest(lvSaved.PointToClient(Cursor.Position));
            if (ht.Item == null) return;
            int col = ht.SubItem != null ? ht.Item.SubItems.IndexOf(ht.SubItem) : 3;
            if (col == 0) EditDesc(ht.Item.Index); else EditValue(ht.Item.Index);
        }

        void EditValue(int i)
        {
            if (i < 0 || i >= saved.Count || h == IntPtr.Zero) return;
            var sv = saved[i];
            string cur = Val.Format(Scanner.ReadValue(h, sv.Addr, sv.Kind), sv.Kind);
            string r = Prompt.Number(this, "값 바꾸기", sv.Desc + "  —  새 값:", cur);
            if (r == null) return;
            double v;
            if (!ParseNum(r, out v)) { Msg("숫자로 넣어 주세요."); return; }
            if (!Scanner.WriteValue(h, sv.Addr, sv.Kind, v)) Msg("값을 쓰지 못했어요.");
            if (sv.Frozen) sv.FrozenVal = v;
            RefreshValues();
        }

        void EditDesc(int i)
        {
            if (i < 0 || i >= saved.Count) return;
            string r = Prompt.Show(this, "설명 바꾸기", "설명 (예: 골드, 주인공 체력):", saved[i].Desc);
            if (string.IsNullOrWhiteSpace(r)) return;
            saved[i].Desc = r.Trim();
            lvSaved.Items[i].Text = saved[i].Desc;
        }

        void EditResults()
        {
            var s = sc;
            if (s == null || busy || s.SnapMode || lvRes.SelectedIndices.Count == 0) return;
            string r = Prompt.Number(this, "값 한꺼번에 바꾸기", "선택한 " + lvRes.SelectedIndices.Count + "개 결과에 넣을 값:", txtVal.Text);
            if (r == null) return;
            double v;
            if (!ParseNum(r, out v)) { Msg("숫자로 넣어 주세요."); return; }
            foreach (int i in lvRes.SelectedIndices)
                if (i < s.Count) Scanner.WriteValue(h, s.Addr[i], (VK)s.Kd[i], v);
            lvRes.Invalidate();
        }

        void DeleteSaved()
        {
            var idx = lvSaved.SelectedIndices.Cast<int>().OrderByDescending(x => x).ToList();
            foreach (int i in idx) { saved.RemoveAt(i); lvSaved.Items.RemoveAt(i); }
        }

        void RefreshValues()
        {
            if (h == IntPtr.Zero || busy) return;
            if (lvRes.VirtualListSize > 0) lvRes.Invalidate();
            for (int i = 0; i < saved.Count && i < lvSaved.Items.Count; i++)
            {
                var sv = saved[i];
                string t = Val.Format(Scanner.ReadValue(h, sv.Addr, sv.Kind), sv.Kind);
                var sub = lvSaved.Items[i].SubItems[3];
                if (sub.Text != t) sub.Text = t;
            }
        }

        void DoFreeze()
        {
            if (h == IntPtr.Zero) return;
            foreach (var sv in saved)
                if (sv.Frozen) Scanner.WriteValue(h, sv.Addr, sv.Kind, sv.FrozenVal);
        }

        // ───────── 자동 업데이트 ─────────
        async void CheckUpdate(bool manual)
        {
            lblStatus.Text = "업데이트 확인 중...";
            string[] r;
            try { r = await Task.Run(() => Updater.FindNewer()); }
            catch
            {
                lblStatus.Text = manual ? "업데이트 확인 실패 (인터넷 연결 확인)" : "준비됨";
                if (manual) Msg("업데이트를 확인하지 못했어요. 인터넷 연결을 확인해 주세요.");
                return;
            }
            if (r == null)
            {
                lblStatus.Text = "준비됨  (최신 버전)";
                if (manual) Msg("지금 최신 버전이에요 (v" + Updater.Current + ").");
                return;
            }
            lblStatus.Text = "새 버전 v" + r[0] + " 받는 중... 다 받으면 자동으로 다시 켜져요";
            try
            {
                await Task.Run(() => Updater.Download(r[1]));
                if (saveEd.Dirty) { lblStatus.Text = "새 버전을 받았어요. 세이브 저장 후 프로그램을 다시 켜면 적용돼요."; return; }
                Detach();
                Updater.SwapAndRestart();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "업데이트 실패: " + ex.Message;
            }
        }
    }
}
