using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MiniCheat
{
    class GameEntry
    {
        public string Root, Title;
        public override string ToString() { return Title + "   (" + Path.GetFileName(Root.TrimEnd('\\', '/')) + ")"; }
    }

    class SaveEditor : UserControl
    {
        GameData db;
        List<SaveSlot> slots = new List<SaveSlot>();
        SaveSlot cur;
        JObj root;
        SaveFmt fmt;
        RubySave rsave;
        DateTime loadedTime;
        bool loading, settingGame;
        public bool Dirty { get; private set; }

        readonly Dictionary<string, Image> iconCache = new Dictionary<string, Image>();

        // 공통
        ComboBox cmbGame;
        CheckBox chkKo;
        GameData trDone;
        bool translating;
        ListView lvSlots;
        TabControl tabs;
        Label lblStatus, lblNoEdit;
        Button btnSave, btnReload;

        // 파티
        ListView lvActors;
        ImageList ilFaces;
        PictureBox picFace;
        TextBox txtName;
        Label lblClass;
        NumericUpDown numLevel, numHp, numMp;
        DataGridView gridParams;
        Panel equipPanel;
        ListView lvSkills;
        ImageList ilSkill;
        JObj actor;

        // 가방
        NumericUpDown numGold;
        RadioButton[] cats;
        CheckBox chkAll;
        TextBox txtBagFind;
        ListView lvBag;
        ImageList ilBag;
        PictureBox picItem;
        Label lblItemName, lblItemDesc;
        NumericUpDown numCount;

        // 스위치·변수
        DataGridView gridSw, gridVar;
        TextBox txtFlagFind;
        CheckBox chkNamed;

        // 기타
        NumericUpDown numH, numM, numS, numSteps, numSaves;
        TreeView tree;
        TextBox txtNode;

        public SaveEditor()
        {
            Font = new Font("맑은 고딕", 9F);

            // ── 위: 게임 고르기 ──
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
            cmbGame = new ComboBox { Width = 380, DropDownStyle = ComboBoxStyle.DropDownList };
            var btnAdd = new Button { Text = "게임 폴더 추가...", Width = 120, Height = 26 };
            var btnBackups = new Button { Text = "백업 목록 · 복원", Width = 120, Height = 26 };
            var btnFolder = new Button { Text = "세이브 폴더 열기", Width = 120, Height = 26 };
            chkKo = new CheckBox { Text = "한글 번역", Checked = true, AutoSize = true, Margin = new Padding(12, 6, 3, 0) };
            top.Controls.AddRange(new Control[] {
                new Label { Text = "게임", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, cmbGame, btnAdd, btnBackups, btnFolder, chkKo });
            chkKo.CheckedChanged += (s, e) =>
            {
                if (db == null) return;
                db.ApplyLang(chkKo.Checked);
                RefreshView();
                RefreshGames(db.Root);
                if (chkKo.Checked && trDone != db) StartTranslate(db);
            };
            cmbGame.SelectedIndexChanged += (s, e) => { var g = cmbGame.SelectedItem as GameEntry; if (g != null && !settingGame) SetGame(g.Root); };
            btnAdd.Click += (s, e) => AddGameDialog();
            btnBackups.Click += (s, e) => ShowBackups();
            btnFolder.Click += (s, e) => { if (db != null && Directory.Exists(db.SaveDir)) Process.Start("explorer.exe", "\"" + db.SaveDir + "\""); };

            // ── 왼쪽: 세이브 목록 ──
            var left = new Panel { Dock = DockStyle.Left, Width = 235, Padding = new Padding(4, 0, 4, 4) };
            lvSlots = new DBListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            lvSlots.Columns.Add("세이브", 72);
            lvSlots.Columns.Add("플레이 시간", 72);
            lvSlots.Columns.Add("저장한 때", 74);
            left.Controls.Add(lvSlots);
            left.Controls.Add(new Label { Text = "세이브 파일", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft });
            lvSlots.SelectedIndexChanged += (s, e) => OnSlotSelected();

            // ── 아래: 저장 ──
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6) };
            btnSave = new Button { Text = "저장하기 (자동 백업)", Dock = DockStyle.Right, Width = 160 };
            btnReload = new Button { Text = "바꾼 것 취소", Dock = DockStyle.Right, Width = 110 };
            lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray, Text = "게임을 고르면 세이브 파일이 왼쪽에 나와요." };
            bottom.Controls.Add(lblStatus); bottom.Controls.Add(btnReload); bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 }); bottom.Controls.Add(btnSave);
            btnSave.Click += (s, e) => SaveNow();
            btnReload.Click += (s, e) => { if (cur != null) { Dirty = false; LoadSlot(cur); } };

            // ── 가운데: 탭 ──
            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildParty());
            tabs.TabPages.Add(BuildBag());
            tabs.TabPages.Add(BuildFlags());
            tabs.TabPages.Add(BuildEtc());
            lblNoEdit = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray, Visible = false };

            var center = new Panel { Dock = DockStyle.Fill };
            center.Controls.Add(tabs); center.Controls.Add(lblNoEdit);

            Controls.Add(center); Controls.Add(left); Controls.Add(bottom); Controls.Add(top);

            RefreshGames(null);
            SetEditEnabled(false);
        }

        // ───────────────────────── 게임 ─────────────────────────
        void RefreshGames(string select)
        {
            settingGame = true;
            cmbGame.Items.Clear();
            foreach (var r in Backup.Games())
            {
                string qt = QuickTitle(r);
                var e = new GameEntry { Root = r, Title = chkKo.Checked ? Translator.Or(qt) : qt };
                cmbGame.Items.Add(e);
                if (select != null && string.Equals(r, select, StringComparison.OrdinalIgnoreCase)) cmbGame.SelectedItem = e;
            }
            settingGame = false;
        }

        static string QuickTitle(string root)
        {
            try
            {
                foreach (var p in new[] { Path.Combine(root, "www", "data", "System.json"), Path.Combine(root, "data", "System.json") })
                {
                    if (!File.Exists(p)) continue;
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(p), "\"gameTitle\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (m.Success && m.Groups[1].Value.Length > 0) return m.Groups[1].Value;
                }
                string ini = Path.Combine(root, "Game.ini");
                if (File.Exists(ini))
                    foreach (var l in File.ReadAllLines(ini, System.Text.Encoding.Default))
                        if (l.StartsWith("Title=", StringComparison.OrdinalIgnoreCase) && l.Length > 6) return l.Substring(6).Trim();
            }
            catch { }
            return Path.GetFileName(root.TrimEnd('\\', '/'));
        }

        // 메모리 검색 탭에서 쯔꾸르 게임을 고르면 불림
        public void OfferGame(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || Dirty) return;
            string d = Path.GetDirectoryName(exePath);
            if (db != null && string.Equals(db.Root, d, StringComparison.OrdinalIgnoreCase)) return;
            SetGame(d);
        }

        void AddGameDialog()
        {
            using (var f = new OpenFileDialog { Title = "게임 폴더 안의 Game.exe (또는 실행 파일)를 골라 주세요", Filter = "게임 실행 파일|*.exe|모든 파일|*.*" })
                if (f.ShowDialog(this) == DialogResult.OK) SetGame(Path.GetDirectoryName(f.FileName));
        }

        bool ConfirmDiscard()
        {
            if (!Dirty) return true;
            var r = MessageBox.Show(this, "바꾼 내용을 아직 저장하지 않았어요. 저장할까요?", "미니 치트", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveNow();
            Dirty = false;
            return true;
        }

        public bool AskClose() { return ConfirmDiscard(); }

        void SetGame(string dir)
        {
            if (!ConfirmDiscard()) { RefreshGames(db == null ? null : db.Root); return; }
            Cursor = Cursors.WaitCursor;
            try
            {
                db = GameData.Load(dir);
                iconCache.Clear();
                // 24px 아이콘 게임은 정확히 2배(48), 32px 게임은 그대로(40칸 안에 32)
                var cell = db.IsJs ? new Size(40, 40) : new Size(48, 48);
                if (ilBag.ImageSize != cell) ilBag.ImageSize = cell;
                ilBag.Images.Clear();
                Backup.RememberGame(dir);
                RefreshGames(dir);
                cur = null; root = null;
                ClearEditor();
                ReloadSlots();
                if (!db.CanEdit)
                {
                    lblNoEdit.Text = db.KindName + " 세이브 수정은 다음 업데이트에서 넣을게요.\n\n지금은 세이브 찾기 · 백업 · 복원을 쓸 수 있어요.";
                    if (db.Kind == RpgKind.Unknown) lblNoEdit.Text = "쯔꾸르 게임 폴더가 아닌 것 같아요.\nGame.exe 가 있는 폴더를 골라 주세요.";
                }
                db.ApplyLang(chkKo.Checked);
                if (chkKo.Checked && db.CanEdit) StartTranslate(db);
                tabs.Visible = db.CanEdit;
                lblNoEdit.Visible = !db.CanEdit;
                lblStatus.Text = db.KindName + " · 세이브 " + slots.Count + "개" + (slots.Count == 0 ? "  (아직 세이브가 없어요. 게임에서 한 번 저장해 주세요)" : " · 왼쪽에서 고르세요");
            }
            catch (Exception ex) { MessageBox.Show(this, "게임을 여는 중 문제가 생겼어요: " + ex.Message, "미니 치트"); }
            Cursor = Cursors.Default;
        }

        // ① 게임 안 한국어 번역 → ② 남은 것 자동 번역 (한 번 한 건 사전에 저장)
        async void StartTranslate(GameData g)
        {
            if (translating) return;
            translating = true;
            int found = 0, auto = 0;
            string err = null;
            try
            {
                if (g.SavedCoversAll())
                {
                    // 전에 번역해서 게임 폴더에 저장해 둔 게 있으면 그걸로 끝
                    trDone = g;
                    g.ApplyLang(chkKo.Checked);
                    RefreshView();
                    RefreshGames(g.Root);
                    lblStatus.Text = "게임 폴더에 저장해 둔 번역(" + GameData.TrFileName + ")을 불러왔어요.";
                    return;
                }
                await System.Threading.Tasks.Task.Run(() => { found = g.FindGameTranslations(); });
                if (g != db) return;
                g.ApplyLang(chkKo.Checked);
                RefreshView();
                var texts = g.TextsForAuto().ToList();
                auto = Translator.CountMissing(texts);
                if (auto > 0)
                {
                    lblStatus.Text = "한글로 자동 번역 중... (이 게임은 처음 한 번만, " + auto + "개)";
                    try
                    {
                        await System.Threading.Tasks.Task.Run(() => Translator.Translate(texts, (d, t) =>
                        {
                            try { BeginInvoke((Action)(() => { if (g == db) lblStatus.Text = "한글로 자동 번역 중... " + d + " / " + t; })); } catch { }
                        }));
                    }
                    catch (Exception ex) { err = ex.Message; }
                }
                if (g != db) return;
                trDone = g;
                g.ApplyLang(chkKo.Checked);
                bool savedToGame = g.SaveTrToGame();
                RefreshView();
                RefreshGames(g.Root);
                var parts = new List<string>();
                if (found > 0) parts.Add("게임에 들어 있는 한국어 번역 " + found + "개를 썼어요");
                if (err != null) parts.Add("자동 번역은 실패했어요 (인터넷 연결 확인)");
                else if (auto > 0) parts.Add((found > 0 ? "나머지 " : "") + auto + "개는 자동 번역했어요");
                if (parts.Count > 0)
                    lblStatus.Text = string.Join(", ", parts) + (savedToGame ? ". 게임 폴더에 번역 파일로 저장해 둬서 다음엔 바로 나와요." : ". (게임 폴더에 쓸 수 없어서 내 문서 사전에만 저장했어요)");
            }
            finally { translating = false; }
        }

        void RefreshView()
        {
            if (root == null) return;
            int actorId = actor == null ? -1 : (int)Json.Num(actor, "_actorId");
            bool d = Dirty;
            FillAll();
            Dirty = d;
            if (actorId >= 0)
                foreach (ListViewItem li in lvActors.Items)
                    if ((int)Json.Num((JObj)li.Tag, "_actorId") == actorId) { li.Selected = true; break; }
        }

        void ReloadSlots()
        {
            string keep = cur == null ? null : cur.Path;
            slots = db == null ? new List<SaveSlot>() : db.FindSaves();
            loading = true;
            lvSlots.BeginUpdate();
            lvSlots.Items.Clear();
            foreach (var s in slots)
            {
                var it = new ListViewItem(new[] { s.Label, s.PlayTime ?? "", s.When }) { Tag = s };
                lvSlots.Items.Add(it);
                if (keep != null && s.Path == keep) it.Selected = true;
            }
            lvSlots.EndUpdate();
            loading = false;
        }

        // ───────────────────────── 세이브 읽기·쓰기 ─────────────────────────
        void OnSlotSelected()
        {
            if (loading || lvSlots.SelectedItems.Count == 0) return;
            var s = (SaveSlot)lvSlots.SelectedItems[0].Tag;
            if (cur != null && s.Path == cur.Path) return;
            if (!ConfirmDiscard()) { ReloadSlots(); return; }
            if (db == null || !db.CanEdit) { cur = s; return; }
            LoadSlot(s);
        }

        void LoadSlot(SaveSlot s)
        {
            try
            {
                if (db.IsRuby) { rsave = RubySave.Load(s.Path, db.Kind); root = rsave.Root; }
                else { rsave = null; root = Json.Parse(SaveCodec.Read(s.Path, out fmt)) as JObj; }
                if (root == null) throw new Exception("세이브 모양이 예상과 달라요");
                cur = s;
                loadedTime = File.GetLastWriteTimeUtc(s.Path);
                Dirty = false;
                FillAll();
                SetEditEnabled(true);
                lblStatus.Text = s.Label + " 을(를) 열었어요. 바꾼 뒤 '저장하기'를 누르면 자동으로 백업하고 저장해요.";
            }
            catch (Exception ex)
            {
                root = null;
                ClearEditor();
                MessageBox.Show(this, "세이브를 읽지 못했어요: " + ex.Message, "미니 치트");
            }
        }

        bool SaveNow()
        {
            if (root == null || cur == null) return false;
            try
            {
                if (File.Exists(cur.Path) && File.GetLastWriteTimeUtc(cur.Path) != loadedTime)
                    if (MessageBox.Show(this, "그 사이에 게임이 이 세이브를 다시 저장했어요.\n그래도 지금 수정한 내용으로 덮어쓸까요? (원래 파일은 백업돼요)", "미니 치트", MessageBoxButtons.YesNo) != DialogResult.Yes)
                        return false;
                Backup.Make(db.BackupName, cur.Path, null);
                if (rsave != null) rsave.Save(cur.Path);
                else SaveCodec.Write(cur.Path, Json.Write(root), fmt);
                loadedTime = File.GetLastWriteTimeUtc(cur.Path);
                Dirty = false;
                btnSave.Enabled = false;
                lblStatus.Text = "저장했어요 (원래 파일은 백업됨). 게임에서 이 세이브를 다시 불러오면 적용돼요.";
                ReloadSlots();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "저장하지 못했어요: " + ex.Message, "미니 치트");
                return false;
            }
        }

        void Changed()
        {
            if (loading) return;
            Dirty = true;
            btnSave.Enabled = true;
            lblStatus.Text = "바꾼 내용이 있어요 — '저장하기'를 눌러야 세이브에 들어가요.";
        }

        void SetEditEnabled(bool on)
        {
            tabs.Enabled = on;
            btnSave.Enabled = on && Dirty;
            btnReload.Enabled = on;
        }

        void ClearEditor()
        {
            loading = true;
            lvActors.Items.Clear(); lvBag.Items.Clear(); lvSkills.Items.Clear();
            gridSw.Rows.Clear(); gridVar.Rows.Clear(); gridParams.Rows.Clear(); equipPanel.Controls.Clear();
            tree.Nodes.Clear();
            picFace.Image = null; picItem.Image = null;
            lblItemName.Text = ""; lblItemDesc.Text = "";
            actor = null;
            loading = false;
            Dirty = false;
            SetEditEnabled(false);
        }

        void FillAll()
        {
            loading = true;
            FillActors();
            numGold.Value = Clamp(Json.Num(Party, "_gold"), numGold);
            FillBag();
            FillFlags();
            FillEtc();
            loading = false;
        }

        static decimal Clamp(double v, NumericUpDown n)
        {
            decimal d = (decimal)Math.Round(v);
            if (d > n.Maximum) n.Maximum = d;
            if (d < n.Minimum) n.Minimum = d;
            return d;
        }

        JObj Party { get { return Json.Obj(root, "party"); } }

        Image Ico(int index, int size)
        {
            string k = index + "_" + size;
            Image im;
            if (!iconCache.TryGetValue(k, out im)) { im = db.Icon(index, size); iconCache[k] = im; }
            return im;
        }

        static NumericUpDown Num(int x, int y, int w, decimal max)
        {
            return new NumericUpDown { Left = x, Top = y, Width = w, Maximum = max, Minimum = 0, ThousandsSeparator = true };
        }

        // ───────────────────────── 파티 ─────────────────────────
        TabPage BuildParty()
        {
            var page = new TabPage("파티 (캐릭터)");
            ilFaces = new ImageList { ImageSize = new Size(64, 64), ColorDepth = ColorDepth.Depth32Bit };
            lvActors = new DBListView { Dock = DockStyle.Left, Width = 160, View = View.LargeIcon, LargeImageList = ilFaces, MultiSelect = false, HideSelection = false };
            lvActors.SelectedIndexChanged += (s, e) => { if (lvActors.SelectedItems.Count > 0) ShowActor((JObj)lvActors.SelectedItems[0].Tag); };

            var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            picFace = new PictureBox { Left = 12, Top = 12, Width = 100, Height = 100, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
            txtName = new TextBox { Left = 165, Top = 12, Width = 150 };
            lblClass = new Label { Left = 242, Top = 47, Width = 80, AutoEllipsis = true, ForeColor = Color.DimGray };
            numLevel = Num(165, 44, 70, 99);
            numLevel.Minimum = 1;
            numHp = Num(165, 76, 100, 9999999);
            numMp = Num(165, 108, 100, 9999999);
            var btnFull = new Button { Text = "HP·MP 가득", Left = 12, Top = 118, Width = 100, Height = 25 };
            p.Controls.AddRange(new Control[] {
                picFace,
                new Label { Text = "이름", Left = 124, Top = 15, AutoSize = true }, txtName, lblClass,
                new Label { Text = "레벨", Left = 124, Top = 47, AutoSize = true }, numLevel,
                new Label { Text = "HP", Left = 124, Top = 79, AutoSize = true }, numHp,
                new Label { Text = "MP", Left = 124, Top = 111, AutoSize = true }, numMp, btnFull });

            gridParams = MakeGrid(12, 152, 310, 232);
            gridParams.Columns.Add("p", "능력치");
            gridParams.Columns.Add("b", "기본");
            gridParams.Columns.Add("a", "+ 추가");
            gridParams.Columns.Add("t", "합계");
            gridParams.Columns[0].ReadOnly = true; gridParams.Columns[1].ReadOnly = true; gridParams.Columns[3].ReadOnly = true;
            gridParams.Columns[0].FillWeight = 130;
            gridParams.Columns[2].DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 230);
            p.Controls.Add(gridParams);
            p.Controls.Add(new Label { Text = "노란 칸을 바꾸면 능력치가 올라요.\n합계는 장비 효과를 뺀 값이에요.", Left = 12, Top = 390, Width = 310, Height = 36, ForeColor = Color.DimGray });

            p.Controls.Add(new Label { Text = "장비", Left = 340, Top = 15, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            equipPanel = new Panel { Left = 340, Top = 36, Width = 320, Height = 200 };
            p.Controls.Add(equipPanel);

            p.Controls.Add(new Label { Text = "배운 스킬", Left = 340, Top = 245, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            ilSkill = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
            lvSkills = new DBListView { Left = 340, Top = 266, Width = 317, Height = 170, View = View.List, SmallImageList = ilSkill, HideSelection = false };
            var btnAddSkill = new Button { Text = "스킬 배우기...", Left = 340, Top = 442, Width = 153, Height = 26 };
            var btnDelSkill = new Button { Text = "고른 스킬 잊기", Left = 504, Top = 442, Width = 153, Height = 26 };
            p.Controls.AddRange(new Control[] { lvSkills, btnAddSkill, btnDelSkill });

            page.Controls.Add(p); page.Controls.Add(lvActors);

            txtName.TextChanged += (s, e) =>
            {
                if (loading || actor == null) return;
                actor["_name"] = JVal.S(txtName.Text);
                if (lvActors.SelectedItems.Count > 0) lvActors.SelectedItems[0].Text = db.T(txtName.Text) + "  Lv." + numLevel.Value;
                Changed();
            };
            numLevel.ValueChanged += (s, e) =>
            {
                if (loading || actor == null) return;
                int lv = (int)numLevel.Value, cls = (int)Json.Num(actor, "_classId", 1);
                actor["_level"] = JVal.N(lv);
                double need = db.ExpFor((int)Json.Num(actor, "_actorId"), cls, lv);
                var exp = Json.Obj(actor, "_exp");
                if (exp != null) exp[cls.ToString()] = JVal.N(need);          // MV·MZ·VX Ace: 직업별
                else if (Json.Get(actor, "_exp") is JVal) actor["_exp"] = JVal.N(need); // VX·XP: 숫자 하나
                if (lvActors.SelectedItems.Count > 0) lvActors.SelectedItems[0].Text = db.T(txtName.Text) + "  Lv." + lv;
                loading = true; FillParams(); loading = false;
                Changed();
            };
            numHp.ValueChanged += (s, e) => { if (!loading && actor != null) { actor["_hp"] = JVal.N((double)numHp.Value); Changed(); } };
            numMp.ValueChanged += (s, e) => { if (!loading && actor != null) { actor[db.MpKey] = JVal.N((double)numMp.Value); Changed(); } };
            btnFull.Click += (s, e) =>
            {
                if (actor == null) return;
                numHp.Value = Clamp(ParamTotal(0), numHp);
                numMp.Value = Clamp(ParamTotal(1), numMp);
            };
            gridParams.CellValueChanged += (s, e) =>
            {
                if (loading || actor == null || e.ColumnIndex != 2 || e.RowIndex < 0) return;
                double v;
                if (!double.TryParse(Convert.ToString(gridParams.Rows[e.RowIndex].Cells[2].Value), out v)) v = 0;
                SetPlus(e.RowIndex, Math.Round(v));
                loading = true; FillParams(); loading = false;
                Changed();
            };
            btnAddSkill.Click += (s, e) => AddSkills();
            btnDelSkill.Click += (s, e) => RemoveSkills();
            return page;
        }

        static DataGridView MakeGrid(int x, int y, int w, int h)
        {
            var g = new DataGridView
            {
                Left = x, Top = y, Width = w, Height = h,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };
            g.DataError += (s, e) => { e.ThrowException = false; };
            return g;
        }

        IEnumerable<JObj> AllActors()
        {
            var data = Json.Arr(Json.Get(root, "actors"), "_data");
            if (data == null) yield break;
            foreach (var n in data.Items) { var o = n as JObj; if (o != null) yield return o; }
        }

        void FillActors()
        {
            lvActors.Items.Clear();
            ilFaces.Images.Clear();
            var partyIds = new List<int>();
            var pa = Json.Arr(Party, "_actors");
            if (pa != null) partyIds = pa.Items.Select(n => n is JObj ? (int)Json.Num(n, "_actorId") : (int)Json.Num(n)).ToList(); // XP 는 캐릭터 자체가 들어 있음
            var all = AllActors().OrderBy(a => { int i = partyIds.IndexOf((int)Json.Num(a, "_actorId")); return i < 0 ? 1000 : i; }).ToList();
            foreach (var a in all)
            {
                bool inParty = partyIds.Contains((int)Json.Num(a, "_actorId"));
                ilFaces.Images.Add(db.Face(Json.Str(a, "_faceName"), (int)Json.Num(a, "_faceIndex"), 64));
                lvActors.Items.Add(new ListViewItem(db.T(Json.Str(a, "_name")) + "  Lv." + Json.Num(a, "_level") + (inParty ? "" : "  (대기)"), ilFaces.Images.Count - 1)
                {
                    Tag = a,
                    ForeColor = inParty ? SystemColors.WindowText : Color.Gray
                });
            }
            if (lvActors.Items.Count > 0) lvActors.Items[0].Selected = true;
            else ShowActor(null);
        }

        void ShowActor(JObj a)
        {
            bool was = loading;
            loading = true;
            actor = a;
            equipPanel.Controls.Clear();
            lvSkills.Items.Clear();
            gridParams.Rows.Clear();
            if (a == null) { picFace.Image = null; loading = was; return; }
            picFace.Image = db.Face(Json.Str(a, "_faceName"), (int)Json.Num(a, "_faceIndex"), 120);
            txtName.Text = Json.Str(a, "_name");
            int cls = (int)Json.Num(a, "_classId", 1);
            lblClass.Text = db.ClassName(cls);
            numLevel.Maximum = Math.Max(db.MaxLevel((int)Json.Num(a, "_actorId")), (int)Json.Num(a, "_level", 1));
            numLevel.Value = Clamp(Math.Max(1, Json.Num(a, "_level", 1)), numLevel);
            numHp.Value = Clamp(Json.Num(a, "_hp"), numHp);
            numMp.Value = Clamp(Json.Num(a, db.MpKey), numMp);
            FillParams();
            FillEquips();
            FillSkills();
            loading = was;
        }

        double GetPlus(int i)
        {
            var keys = db.PlusKeys;
            return keys != null ? Json.Num(actor, keys[i]) : Json.Num(Json.At(Json.Arr(actor, "_paramPlus"), i));
        }

        void SetPlus(int i, double v)
        {
            var keys = db.PlusKeys;
            if (keys != null) { actor[keys[i]] = JVal.N(v); return; }
            var plus = Json.Arr(actor, "_paramPlus");
            if (plus == null) { plus = new JArr(); actor["_paramPlus"] = plus; }
            Json.SetAt(plus, i, JVal.N(v));
        }

        int BaseOf(int i)
        {
            return db.BaseParam((int)Json.Num(actor, "_actorId"), (int)Json.Num(actor, "_classId", 1), i, (int)Json.Num(actor, "_level", 1));
        }

        double ParamTotal(int i) { return BaseOf(i) + GetPlus(i); }

        void FillParams()
        {
            gridParams.Rows.Clear();
            if (actor == null) return;
            for (int i = 0; i < db.ParamCount; i++)
            {
                int b = BaseOf(i);
                double pl = GetPlus(i);
                gridParams.Rows.Add(db.ParamNames[i], b, pl, b + pl);
            }
        }

        // 장비 칸 하나: 종류(1 무기, 2 방패, 3 머리, 4 몸, 5 장신구), 지금 장비, 바꾸기
        class EquipSlot { public int EType; public DbItem Cur; public Action<DbItem> Set; }

        List<EquipSlot> EquipSlots()
        {
            var list = new List<EquipSlot>();
            if (actor == null) return list;
            if (db.Kind == RpgKind.VX || db.Kind == RpgKind.XP)
            {
                // VX·XP: 무기 번호, 방어구1~4 번호가 따로 저장됨
                string[] keys = { "_weaponId", "_armor1Id", "_armor2Id", "_armor3Id", "_armor4Id" };
                for (int i = 0; i < keys.Length; i++)
                {
                    if (Json.Get(actor, keys[i]) == null) continue;
                    string k = keys[i];
                    var src = i == 0 ? db.Weapons : db.Armors;
                    DbItem cur;
                    src.TryGetValue((int)Json.Num(actor, k), out cur);
                    list.Add(new EquipSlot { EType = i + 1, Cur = cur, Set = it => actor[k] = JVal.N(it == null ? 0 : it.Id) });
                }
                return list;
            }
            var eq = Json.Arr(actor, "_equips");
            if (eq == null) return list;
            bool ace = db.Kind == RpgKind.VXAce;
            for (int i = 0; i < eq.Items.Count; i++)
            {
                var gi = eq.Items[i] as JObj;
                // MV·MZ: _dataClass "weapon"/"armor",  VX Ace: _class 가 RPG::Weapon / RPG::Armor
                string cls = ace ? (Json.Str(gi, "_class").EndsWith("RPG::Weapon") ? "weapon" : Json.Str(gi, "_class").EndsWith("RPG::Armor") ? "armor" : "")
                                 : Json.Str(gi, "_dataClass");
                int et = i == 1 && cls == "weapon" ? 1 : i + 1; // 쌍수면 두 번째 칸도 무기
                DbItem cur = null;
                int id = (int)Json.Num(gi, "_itemId");
                if (id > 0) (cls == "weapon" ? db.Weapons : db.Armors).TryGetValue(id, out cur);
                int slot = i;
                list.Add(new EquipSlot
                {
                    EType = et, Cur = cur,
                    Set = it =>
                    {
                        var g = Json.At(eq, slot) as JObj;
                        if (g == null) { g = new JObj(); g["@"] = JVal.S(ace ? "Game_BaseItem" : "Game_Item"); Json.SetAt(eq, slot, g); }
                        if (ace) g["_class"] = it == null ? (JNode)new JVal { Raw = "null" } : JVal.S(RubyBridge.ClassTag + (it.Kind == 1 ? "RPG::Weapon" : "RPG::Armor"));
                        else g["_dataClass"] = JVal.S(it == null ? "" : it.Kind == 1 ? "weapon" : "armor");
                        g["_itemId"] = JVal.N(it == null ? 0 : it.Id);
                    }
                });
            }
            return list;
        }

        void FillEquips()
        {
            var slots = EquipSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                int et = sl.EType;
                var label = new Label { Text = et < db.EquipTypes.Length && db.EquipTypes[et].Length > 0 ? db.EquipTypes[et] : "장비 " + (i + 1), Left = 0, Top = i * 32 + 5, Width = 70, AutoEllipsis = true };
                var cb = MakeIconCombo(72, i * 32, 245);
                cb.Items.Add("(없음)");
                var pool = et == 1 ? db.Weapons.Values : db.Armors.Values.Where(x => x.EType == et);
                foreach (var it in pool) cb.Items.Add(it);
                if (sl.Cur != null && !cb.Items.Contains(sl.Cur)) cb.Items.Add(sl.Cur);
                cb.SelectedItem = sl.Cur != null ? (object)sl.Cur : "(없음)";
                cb.SelectedIndexChanged += (s, e) =>
                {
                    if (loading || actor == null) return;
                    sl.Set(cb.SelectedItem as DbItem);
                    Changed();
                };
                equipPanel.Controls.Add(label);
                equipPanel.Controls.Add(cb);
            }
            equipPanel.Height = Math.Max(40, slots.Count * 32 + 4);
        }

        ComboBox MakeIconCombo(int x, int y, int w)
        {
            var c = new ComboBox { Left = x, Top = y, Width = w, DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24, MaxDropDownItems = 15 };
            c.DrawItem += (s, e) =>
            {
                e.DrawBackground();
                if (e.Index >= 0)
                {
                    var o = c.Items[e.Index];
                    var it = o as DbItem;
                    if (it != null) e.Graphics.DrawImage(Ico(it.Icon, 24), e.Bounds.X + 2, e.Bounds.Y + (e.Bounds.Height - 24) / 2, 24, 24);
                    var col = (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : SystemColors.WindowText;
                    TextRenderer.DrawText(e.Graphics, o.ToString(), e.Font, new Rectangle(e.Bounds.X + 30, e.Bounds.Y, e.Bounds.Width - 32, e.Bounds.Height), col,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                e.DrawFocusRectangle();
            };
            return c;
        }

        void FillSkills()
        {
            lvSkills.Items.Clear();
            ilSkill.Images.Clear();
            var sk = Json.Arr(actor, "_skills");
            if (sk == null) return;
            foreach (var n in sk.Items)
            {
                int id = (int)Json.Num(n);
                DbItem s;
                db.Skills.TryGetValue(id, out s);
                ilSkill.Images.Add(Ico(s == null ? 0 : s.Icon, 24));
                lvSkills.Items.Add(new ListViewItem(s == null ? "스킬 #" + id : s.Name, ilSkill.Images.Count - 1) { Tag = id, ToolTipText = s == null ? "" : s.Desc });
            }
        }

        void AddSkills()
        {
            if (actor == null) return;
            var sk = Json.Arr(actor, "_skills");
            if (sk == null) { sk = new JArr(); actor["_skills"] = sk; }
            var have = new HashSet<int>(sk.Items.Select(n => (int)Json.Num(n)));
            var picked = PickDialog.Pick(this, "스킬 배우기", db.Skills.Values.Where(x => !have.Contains(x.Id)), Ico);
            if (picked.Count == 0) return;
            foreach (var p in picked) sk.Items.Add(JVal.N(p.Id));
            var sorted = sk.Items.OrderBy(n => Json.Num(n)).ToList();
            sk.Items.Clear(); sk.Items.AddRange(sorted);
            loading = true; FillSkills(); loading = false;
            Changed();
        }

        void RemoveSkills()
        {
            if (actor == null || lvSkills.SelectedItems.Count == 0) return;
            var sk = Json.Arr(actor, "_skills");
            var del = new HashSet<int>(lvSkills.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag));
            sk.Items.RemoveAll(n => del.Contains((int)Json.Num(n)));
            FillSkills();
            Changed();
        }

        // ───────────────────────── 가방 ─────────────────────────
        TabPage BuildBag()
        {
            var page = new TabPage("가방 · 돈");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(4, 6, 4, 0) };
            numGold = new NumericUpDown { Width = 130, Maximum = 99999999, ThousandsSeparator = true };
            var btnGoldMax = new Button { Text = "최대로", Width = 70, Height = 25 };
            top.Controls.AddRange(new Control[] { new Label { Text = "돈", AutoSize = true, Margin = new Padding(3, 6, 3, 0), Font = new Font(Font, FontStyle.Bold) }, numGold, btnGoldMax });
            top.SetFlowBreak(btnGoldMax, true);
            string[] names = { "아이템", "무기", "방어구", "중요 아이템" };
            cats = new RadioButton[4];
            for (int i = 0; i < 4; i++)
            {
                cats[i] = new RadioButton { Text = names[i], Appearance = Appearance.Button, Width = 90, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Checked = i == 0 };
                cats[i].CheckedChanged += (s, e) => { if (((RadioButton)s).Checked) FillBag(); };
                top.Controls.Add(cats[i]);
            }
            chkAll = new CheckBox { Text = "안 가진 것도 보기", AutoSize = true, Margin = new Padding(12, 6, 3, 0) };
            txtBagFind = new TextBox { Width = 140, Margin = new Padding(3, 3, 3, 0) };
            top.Controls.AddRange(new Control[] { chkAll, new Label { Text = "찾기", AutoSize = true, Margin = new Padding(10, 6, 0, 0) }, txtBagFind });

            ilBag = new ImageList { ImageSize = new Size(40, 40), ColorDepth = ColorDepth.Depth32Bit };
            lvBag = new DBListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = ilBag, HideSelection = false };

            var right = new Panel { Dock = DockStyle.Right, Width = 230, Padding = new Padding(8) };
            picItem = new PictureBox { Left = 10, Top = 6, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.CenterImage };
            lblItemName = new Label { Left = 82, Top = 10, Width = 140, Height = 40, Font = new Font(Font.FontFamily, 10F, FontStyle.Bold), AutoEllipsis = true };
            lblItemDesc = new Label { Left = 10, Top = 76, Width = 212, Height = 66, ForeColor = Color.DimGray, AutoEllipsis = true };
            numCount = new NumericUpDown { Left = 50, Top = 146, Width = 90, Maximum = 9999 };
            var b1 = new Button { Text = "+1", Left = 10, Top = 178, Width = 50, Height = 26 };
            var b10 = new Button { Text = "+10", Left = 66, Top = 178, Width = 50, Height = 26 };
            var b99 = new Button { Text = "99개", Left = 122, Top = 178, Width = 50, Height = 26 };
            var b0 = new Button { Text = "0개", Left = 178, Top = 178, Width = 44, Height = 26 };
            var bAll = new Button { Text = "보이는 것 전부 99개", Left = 10, Top = 230, Width = 212, Height = 30 };
            right.Controls.AddRange(new Control[] { picItem, lblItemName, lblItemDesc, new Label { Text = "개수", Left = 10, Top = 149, AutoSize = true }, numCount, b1, b10, b99, b0, bAll,
                new Label { Text = "아이템을 클릭하고 개수를 바꾸세요.\n'안 가진 것도 보기'를 켜면 게임에 있는 모든 아이템이 흐리게 나와요.", Left = 10, Top = 272, Width = 212, Height = 80, ForeColor = Color.DimGray } });

            page.Controls.Add(lvBag); page.Controls.Add(right); page.Controls.Add(top);

            numGold.ValueChanged += (s, e) => { if (!loading && root != null) { Json.SetNum(Party, "_gold", (double)numGold.Value); Changed(); } };
            btnGoldMax.Click += (s, e) => numGold.Value = numGold.Maximum;
            chkAll.CheckedChanged += (s, e) => FillBag();
            txtBagFind.TextChanged += (s, e) => FillBag();
            lvBag.SelectedIndexChanged += (s, e) => ShowItem();
            numCount.ValueChanged += (s, e) =>
            {
                if (loading || lvBag.SelectedItems.Count == 0) return;
                foreach (ListViewItem li in lvBag.SelectedItems) SetCount((DbItem)li.Tag, (int)numCount.Value, li);
            };
            b1.Click += (s, e) => numCount.Value = Math.Min(numCount.Maximum, numCount.Value + 1);
            b10.Click += (s, e) => numCount.Value = Math.Min(numCount.Maximum, numCount.Value + 10);
            b99.Click += (s, e) => numCount.Value = 99;
            b0.Click += (s, e) => numCount.Value = 0;
            bAll.Click += (s, e) =>
            {
                if (root == null || lvBag.Items.Count == 0) return;
                if (MessageBox.Show(this, "지금 보이는 " + lvBag.Items.Count + "개를 모두 99개로 할까요?", "미니 치트", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                foreach (ListViewItem li in lvBag.Items) SetCount((DbItem)li.Tag, 99, li);
            };
            return page;
        }

        int Cat { get { for (int i = 0; i < 4; i++) if (cats[i].Checked) return i; return 0; } }

        JObj Bag(DbItem it)
        {
            string k = it.Kind == 1 ? "_weapons" : it.Kind == 2 ? "_armors" : "_items";
            var o = Json.Obj(Party, k);
            if (o == null && Party != null) { o = new JObj(); Party[k] = o; }
            return o;
        }

        int Count(DbItem it) { return (int)Json.Num(Json.Get(Bag(it), it.Id.ToString())); }

        void SetCount(DbItem it, int n, ListViewItem li)
        {
            var c = Bag(it);
            if (c == null) return;
            if (n <= 0) c.Remove(it.Id.ToString()); else c[it.Id.ToString()] = JVal.N(n);
            li.Text = it.Name + "  ×" + n;
            li.ForeColor = n > 0 ? SystemColors.WindowText : Color.Silver;
            Changed();
        }

        void FillBag()
        {
            if (root == null || db == null) return;
            bool was = loading;
            loading = true;
            int cat = Cat;
            var src = cat == 1 ? db.Weapons : cat == 2 ? db.Armors : db.Items;
            int kind = cat == 1 ? 1 : cat == 2 ? 2 : 0;
            var list = src.Values.Where(x => cat == 3 ? x.IType == 2 : cat == 0 ? x.IType != 2 : true).ToList();
            // 데이터베이스에 없는데 세이브에 있는 것
            var cont = Json.Obj(Party, kind == 1 ? "_weapons" : kind == 2 ? "_armors" : "_items");
            if (cont != null && cat != 3)
                foreach (var k in cont.Keys)
                {
                    int id;
                    if (int.TryParse(k, out id) && !src.ContainsKey(id)) list.Add(new DbItem { Id = id, Name = "알 수 없는 것 #" + id, Kind = kind });
                }
            string f = txtBagFind.Text.Trim();
            lvBag.BeginUpdate();
            lvBag.Items.Clear();
            foreach (var it in list.OrderBy(x => x.Id))
            {
                int n = Count(it);
                if (n <= 0 && !chkAll.Checked) continue;
                if (f.Length > 0 && it.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string key = "i" + it.Icon;
                if (!ilBag.Images.ContainsKey(key)) ilBag.Images.Add(key, Ico(it.Icon, ilBag.ImageSize.Width));
                lvBag.Items.Add(new ListViewItem(it.Name + "  ×" + n) { Tag = it, ImageKey = key, ToolTipText = it.Desc, ForeColor = n > 0 ? SystemColors.WindowText : Color.Silver });
            }
            lvBag.EndUpdate();
            picItem.Image = null; lblItemName.Text = ""; lblItemDesc.Text = "";
            loading = was;
        }

        void ShowItem()
        {
            if (lvBag.SelectedItems.Count == 0) return;
            var it = (DbItem)lvBag.SelectedItems[0].Tag;
            loading = true;
            picItem.Image = Ico(it.Icon, 64);
            lblItemName.Text = lvBag.SelectedItems.Count > 1 ? it.Name + " 외 " + (lvBag.SelectedItems.Count - 1) + "개" : it.Name;
            string orig = GameData.Clean(it.RawName);
            lblItemDesc.Text = it.Desc + (db.Ko && orig.Length > 0 && orig != it.Name ? "\n원문: " + orig : "");
            numCount.Value = Clamp(Count(it), numCount);
            loading = false;
        }

        // ───────────────────────── 스위치·변수 ─────────────────────────
        TabPage BuildFlags()
        {
            var page = new TabPage("스위치 · 변수");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4, 6, 4, 0) };
            txtFlagFind = new TextBox { Width = 180 };
            chkNamed = new CheckBox { Text = "이름 붙은 것만", AutoSize = true, Checked = true, Margin = new Padding(12, 4, 3, 0) };
            top.Controls.AddRange(new Control[] { new Label { Text = "이름·번호 찾기", AutoSize = true, Margin = new Padding(3, 5, 3, 0) }, txtFlagFind, chkNamed,
                new Label { Text = "스위치는 켜고 끄기(체크), 변수는 숫자를 바꾸세요.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 5, 3, 0) } });

            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Resize += (s, e) => { if (split.Width > 200) split.SplitterDistance = split.Width / 2; };
            gridSw = MakeGrid(0, 0, 10, 10); gridSw.Dock = DockStyle.Fill;
            gridSw.Columns.Add("n", "번호");
            gridSw.Columns.Add("name", "스위치 이름");
            gridSw.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "켜짐" });
            gridVar = MakeGrid(0, 0, 10, 10); gridVar.Dock = DockStyle.Fill;
            gridVar.Columns.Add("n", "번호");
            gridVar.Columns.Add("name", "변수 이름");
            gridVar.Columns.Add("v", "값");
            foreach (var g in new[] { gridSw, gridVar })
            {
                g.Columns[0].ReadOnly = true; g.Columns[1].ReadOnly = true;
                g.Columns[0].FillWeight = 22; g.Columns[1].FillWeight = 100; g.Columns[2].FillWeight = 35;
                g.Columns[2].DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 230);
            }
            split.Panel1.Controls.Add(gridSw);
            split.Panel1.Controls.Add(new Label { Text = "스위치", Dock = DockStyle.Top, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            split.Panel2.Controls.Add(gridVar);
            split.Panel2.Controls.Add(new Label { Text = "변수", Dock = DockStyle.Top, Height = 20, Font = new Font(Font, FontStyle.Bold) });
            page.Controls.Add(split); page.Controls.Add(top);

            txtFlagFind.TextChanged += (s, e) => FillFlags();
            chkNamed.CheckedChanged += (s, e) => FillFlags();
            gridSw.CurrentCellDirtyStateChanged += (s, e) => { if (gridSw.IsCurrentCellDirty) gridSw.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            gridSw.CellValueChanged += (s, e) =>
            {
                if (loading || e.RowIndex < 0 || e.ColumnIndex != 2) return;
                var data = Json.Arr(Json.Get(root, "switches"), "_data");
                if (data == null) return;
                Json.SetAt(data, (int)gridSw.Rows[e.RowIndex].Tag, JVal.B(Convert.ToBoolean(gridSw.Rows[e.RowIndex].Cells[2].Value)));
                Changed();
            };
            gridVar.CellValueChanged += (s, e) =>
            {
                if (loading || e.RowIndex < 0 || e.ColumnIndex != 2) return;
                var data = Json.Arr(Json.Get(root, "variables"), "_data");
                if (data == null) return;
                double v;
                var cell = gridVar.Rows[e.RowIndex].Cells[2];
                if (!double.TryParse(Convert.ToString(cell.Value), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    loading = true; cell.Value = "0"; loading = false; v = 0;
                }
                Json.SetAt(data, (int)gridVar.Rows[e.RowIndex].Tag, JVal.N(v));
                Changed();
            };
            return page;
        }

        void FillFlags()
        {
            if (root == null || db == null) return;
            bool was = loading;
            loading = true;
            string f = txtFlagFind.Text.Trim();
            FillFlagGrid(gridSw, Json.Arr(Json.Get(root, "switches"), "_data"), db.SwitchNames, f, true);
            FillFlagGrid(gridVar, Json.Arr(Json.Get(root, "variables"), "_data"), db.VarNames, f, false);
            loading = was;
        }

        void FillFlagGrid(DataGridView g, JArr data, string[] names, string find, bool isSwitch)
        {
            g.Rows.Clear();
            int count = Math.Max(names.Length, data == null ? 0 : data.Items.Count);
            bool namedOnly = chkNamed.Checked && names.Any(n => n.Length > 0);
            var rows = new List<DataGridViewRow>();
            for (int i = 1; i < count; i++)
            {
                string name = i < names.Length ? names[i] : "";
                if (namedOnly && name.Length == 0) continue;
                if (find.Length > 0 && name.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0 && i.ToString() != find) continue;
                var cell = Json.At(data, i) as JVal;
                var r = new DataGridViewRow();
                r.CreateCells(g);
                r.Cells[0].Value = i;
                r.Cells[1].Value = name;
                var rawArr = isSwitch ? db.swRaw : db.varRaw;
                if (db.Ko && i < rawArr.Length && GameData.Clean(rawArr[i]) != name) r.Cells[1].ToolTipText = "원문: " + GameData.Clean(rawArr[i]);
                if (isSwitch) r.Cells[2].Value = cell != null && cell.Raw == "true";
                else
                {
                    if (cell == null || cell.Raw == "null") r.Cells[2].Value = "0";
                    else if (cell.IsNum) r.Cells[2].Value = cell.Raw;
                    else { r.Cells[2].Value = cell.IsStr ? cell.Str : cell.Raw; r.Cells[2].ReadOnly = true; }
                    if (Json.At(data, i) is JObj || Json.At(data, i) is JArr) { r.Cells[2].Value = "(목록 데이터)"; r.Cells[2].ReadOnly = true; }
                }
                r.Tag = i;
                rows.Add(r);
            }
            g.Rows.AddRange(rows.ToArray());
        }

        // ───────────────────────── 기타 ─────────────────────────
        TabPage BuildEtc()
        {
            var page = new TabPage("기타 · 전체 데이터");
            var top = new Panel { Dock = DockStyle.Top, Height = 80 };
            numH = Num(90, 10, 60, 99999); numM = Num(170, 10, 50, 59); numS = Num(240, 10, 50, 59);
            numSteps = Num(90, 44, 110, 999999999); numSaves = Num(310, 44, 80, 999999);
            top.Controls.AddRange(new Control[] {
                new Label { Text = "플레이 시간", Left = 10, Top = 13, AutoSize = true }, numH,
                new Label { Text = "시간", Left = 152, Top = 13, AutoSize = true }, numM,
                new Label { Text = "분", Left = 222, Top = 13, AutoSize = true }, numS,
                new Label { Text = "초", Left = 292, Top = 13, AutoSize = true },
                new Label { Text = "걸음 수", Left = 10, Top = 47, AutoSize = true }, numSteps,
                new Label { Text = "저장 횟수", Left = 230, Top = 47, AutoSize = true }, numSaves });
            EventHandler playChanged = (s, e) =>
            {
                if (loading || root == null) return;
                double sec = (double)(numH.Value * 3600 + numM.Value * 60 + numS.Value);
                if (Json.Get(root, "frameCount") != null) root["frameCount"] = JVal.N(sec * Fps); // VX·XP
                else Json.SetNum(Json.Get(root, "system"), "_framesOnSave", sec * Fps);
                Changed();
            };
            numH.ValueChanged += playChanged; numM.ValueChanged += playChanged; numS.ValueChanged += playChanged;
            numSteps.ValueChanged += (s, e) => { if (!loading && root != null) { Json.SetNum(Party, "_steps", (double)numSteps.Value); Changed(); } };
            numSaves.ValueChanged += (s, e) => { if (!loading && root != null) { Json.SetNum(Json.Get(root, "system"), "_saveCount", (double)numSaves.Value); Changed(); } };

            var edit = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(4, 4, 4, 0) };
            txtNode = new TextBox { Width = 300 };
            var btnApply = new Button { Text = "이 값으로 바꾸기", Width = 120, Height = 25 };
            edit.Controls.AddRange(new Control[] { new Label { Text = "고른 값", AutoSize = true, Margin = new Padding(3, 6, 3, 0) }, txtNode, btnApply,
                new Label { Text = "세이브 안의 모든 값 (고급). 위 탭에 없는 것도 여기서 바꿀 수 있어요.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 6, 3, 0) } });
            tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            tree.BeforeExpand += (s, e) => ExpandNode(e.Node);
            tree.AfterSelect += (s, e) =>
            {
                var v = e.Node.Tag as JVal;
                txtNode.Enabled = btnApply.Enabled = v != null;
                txtNode.Text = v == null ? "" : v.IsStr ? v.Str : v.Raw;
            };
            btnApply.Click += (s, e) => ApplyNode();
            page.Controls.Add(tree); page.Controls.Add(edit); page.Controls.Add(top);
            return page;
        }

        int Fps { get { return db.Kind == RpgKind.XP ? 40 : 60; } }

        double Frames()
        {
            if (Json.Get(root, "frameCount") != null) return Json.Num(root, "frameCount");
            return Json.Num(Json.Get(root, "system"), "_framesOnSave");
        }

        void FillEtc()
        {
            long sec = (long)(Frames() / Fps);
            numH.Value = Clamp(sec / 3600, numH); numM.Value = sec / 60 % 60; numS.Value = sec % 60;
            numSteps.Value = Clamp(Json.Num(Party, "_steps"), numSteps);
            numSaves.Value = Clamp(Json.Num(Json.Get(root, "system"), "_saveCount"), numSaves);

            tree.Nodes.Clear();
            var r = new TreeNode("세이브 전체") { Tag = root };
            r.Nodes.Add(new TreeNode("..."));
            tree.Nodes.Add(r);
            r.Expand();
        }

        static string NodeLabel(string k, JNode n)
        {
            var v = n as JVal;
            if (v != null) return k + " : " + (v.IsStr ? "\"" + v.Str + "\"" : v.Raw);
            var o = n as JObj;
            if (o != null)
            {
                var cls = Json.Str(o, "@");
                var arr = Json.Get(o, "@a") as JArr;
                if (arr != null) return k + "  [" + arr.Items.Count + "]";
                return k + (cls.Length > 0 ? "  (" + cls + ")" : "  {" + o.Keys.Count(x => !x.StartsWith("@")) + "}");
            }
            return k + "  [" + ((JArr)n).Items.Count + "]";
        }

        void ExpandNode(TreeNode tn)
        {
            if (tn.Nodes.Count != 1 || tn.Nodes[0].Tag != null || tn.Nodes[0].Text != "...") return;
            tn.Nodes.Clear();
            var n = tn.Tag as JNode;
            var o = n as JObj;
            IEnumerable<KeyValuePair<string, JNode>> kids;
            if (o != null && Json.Get(o, "@a") is JArr) n = Json.Get(o, "@a");
            o = n as JObj;
            if (o != null) kids = o.Keys.Where(k => !k.StartsWith("@")).Select(k => new KeyValuePair<string, JNode>(k, o.Map[k]));
            else kids = ((JArr)n).Items.Select((x, i) => new KeyValuePair<string, JNode>("[" + i + "]", x));
            var list = new List<TreeNode>();
            foreach (var kv in kids.Take(5000))
            {
                var c = new TreeNode(NodeLabel(kv.Key, kv.Value)) { Tag = kv.Value, Name = kv.Key };
                if (!(kv.Value is JVal)) c.Nodes.Add(new TreeNode("..."));
                list.Add(c);
            }
            tn.Nodes.AddRange(list.ToArray());
        }

        void ApplyNode()
        {
            var tn = tree.SelectedNode;
            var v = tn == null ? null : tn.Tag as JVal;
            if (v == null) return;
            string t = txtNode.Text;
            if (v.IsStr) v.Str = t;
            else if (v.IsBool)
            {
                if (t != "true" && t != "false") { MessageBox.Show(this, "true 또는 false 로 넣어 주세요.", "미니 치트"); return; }
                v.Raw = t;
            }
            else
            {
                double d;
                if (t == "null") v.Raw = "null";
                else if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) v.Raw = JVal.Fmt(d);
                else { MessageBox.Show(this, "숫자로 넣어 주세요.", "미니 치트"); return; }
            }
            tn.Text = NodeLabel(tn.Name, v);
            Changed();
            // 위 탭들도 새 값으로
            var keepActor = actor;
            loading = true; FillBag(); FillFlags(); if (keepActor != null) ShowActor(keepActor); loading = false;
        }

        // ───────────────────────── 백업 ─────────────────────────
        void ShowBackups()
        {
            if (db == null) { MessageBox.Show(this, "먼저 게임을 골라 주세요.", "미니 치트"); return; }
            using (var f = new BackupForm(db, slots))
            {
                f.ShowDialog(this);
                if (f.Restored)
                {
                    ReloadSlots();
                    if (cur != null && File.Exists(cur.Path)) { Dirty = false; LoadSlot(cur); }
                }
            }
        }
    }

    // 여러 개 골라 추가하는 창 (스킬 등)
    static class PickDialog
    {
        public static List<DbItem> Pick(IWin32Window owner, string title, IEnumerable<DbItem> items, Func<int, int, Image> ico)
        {
            var all = items.OrderBy(x => x.Id).ToList();
            using (var f = new Form())
            {
                f.SuspendLayout();
                f.AutoScaleDimensions = new SizeF(96F, 96F);
                f.AutoScaleMode = AutoScaleMode.Dpi;
                f.Font = new Font("맑은 고딕", 9F);
                f.Text = title; f.ClientSize = new Size(420, 500); f.StartPosition = FormStartPosition.CenterParent; f.MinimizeBox = false;
                var top = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 4) };
                var txt = new TextBox { Dock = DockStyle.Fill };
                top.Controls.Add(txt);
                top.Controls.Add(new Label { Text = "찾기", Dock = DockStyle.Left, Width = 40, TextAlign = ContentAlignment.MiddleLeft });
                var il = new ImageList { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
                var lv = new DBListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, SmallImageList = il, HideSelection = false, HeaderStyle = ColumnHeaderStyle.None };
                lv.Columns.Add("", 380);
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
                var no = new Button { Text = "취소", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
                var ok = new Button { Text = "추가", Width = 90, Height = 28, DialogResult = DialogResult.OK };
                bottom.Controls.Add(no); bottom.Controls.Add(ok);
                bottom.Controls.Add(new Label { Text = "Ctrl·Shift 로 여러 개 고르기", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 8, 10, 0) });
                f.Controls.Add(lv); f.Controls.Add(bottom); f.Controls.Add(top);
                f.AcceptButton = ok; f.CancelButton = no;
                Action fill = () =>
                {
                    lv.BeginUpdate(); lv.Items.Clear();
                    foreach (var it in all)
                    {
                        if (txt.Text.Length > 0 && it.Name.IndexOf(txt.Text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string k = "i" + it.Icon;
                        if (!il.Images.ContainsKey(k)) il.Images.Add(k, ico(it.Icon, 24));
                        lv.Items.Add(new ListViewItem(it.Name + (it.Desc.Length > 0 ? "   —  " + it.Desc : "")) { Tag = it, ImageKey = k });
                    }
                    lv.EndUpdate();
                };
                txt.TextChanged += (s, e) => fill();
                lv.DoubleClick += (s, e) => f.DialogResult = DialogResult.OK;
                fill();
                f.ResumeLayout(false);
                if (f.ShowDialog(owner) != DialogResult.OK) return new List<DbItem>();
                return lv.SelectedItems.Cast<ListViewItem>().Select(i => (DbItem)i.Tag).ToList();
            }
        }
    }

    class BackupForm : Form
    {
        public bool Restored;
        readonly GameData db;
        readonly List<SaveSlot> slots;
        readonly ListView lv;

        public BackupForm(GameData db, List<SaveSlot> slots)
        {
            this.db = db; this.slots = slots;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("맑은 고딕", 9F);
            Text = "백업 목록 — " + db.BackupName;
            ClientSize = new Size(560, 440);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            lv = new DBListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            lv.Columns.Add("백업한 때", 220);
            lv.Columns.Add("파일", 160);
            lv.Columns.Add("크기", 80);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6) };
            var bRestore = new Button { Text = "이 백업으로 되돌리기", Width = 150, Height = 28 };
            var bNow = new Button { Text = "지금 전부 백업", Width = 110, Height = 28 };
            var bDel = new Button { Text = "삭제", Width = 60, Height = 28 };
            var bOpen = new Button { Text = "백업 폴더 열기", Width = 110, Height = 28 };
            var bClose = new Button { Text = "닫기", Width = 60, Height = 28, DialogResult = DialogResult.Cancel };
            bottom.Controls.AddRange(new Control[] { bRestore, bNow, bDel, bOpen, bClose });
            var info = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 4, 8, 0), ForeColor = Color.DimGray,
                Text = "저장할 때마다 원래 파일이 자동으로 여기 보관돼요 (게임마다 최근 " + Backup.Keep + "개).\n되돌리기 전에 지금 파일도 한 번 더 백업하니 안심하고 눌러도 돼요." };
            Controls.Add(lv); Controls.Add(bottom); Controls.Add(info);
            CancelButton = bClose;

            bRestore.Click += (s, e) => RestoreSel();
            lv.DoubleClick += (s, e) => RestoreSel();
            bNow.Click += (s, e) =>
            {
                foreach (var sl in slots) Backup.Make(db.BackupName, sl.Path, "수동");
                Fill();
            };
            bDel.Click += (s, e) =>
            {
                if (lv.SelectedItems.Count == 0) return;
                var p = (string)lv.SelectedItems[0].Tag;
                try { File.Delete(p); var d = Path.GetDirectoryName(p); if (Directory.GetFiles(d).Length == 0) Directory.Delete(d); } catch { }
                Fill();
            };
            bOpen.Click += (s, e) => { var d = Backup.Dir(db.BackupName); Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); };
            Fill();
            ResumeLayout(false);
        }

        void Fill()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var b in Backup.List(db.BackupName))
            {
                long size = 0;
                try { size = new FileInfo(b[2]).Length; } catch { }
                lv.Items.Add(new ListViewItem(new[] { b[0], b[1], (size / 1024.0).ToString("0.0") + " KB" }) { Tag = b[2] });
            }
            lv.EndUpdate();
        }

        void RestoreSel()
        {
            if (lv.SelectedItems.Count == 0) return;
            string src = (string)lv.SelectedItems[0].Tag;
            string dst = Path.Combine(db.SaveDir, Path.GetFileName(src));
            if (MessageBox.Show(this, Path.GetFileName(src) + " 을(를) " + lv.SelectedItems[0].Text + " 상태로 되돌릴까요?", "미니 치트", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                Directory.CreateDirectory(db.SaveDir);
                string tmp = Path.GetTempFileName();
                File.Copy(src, tmp, true); // 백업 정리로 원본이 지워져도 안전하게
                if (File.Exists(dst)) Backup.Make(db.BackupName, dst, "되돌리기 전");
                File.Copy(tmp, dst, true);
                File.Delete(tmp);
                Restored = true;
                Fill();
                MessageBox.Show(this, "되돌렸어요. 게임에서 이 세이브를 다시 불러오세요.", "미니 치트");
            }
            catch (Exception ex) { MessageBox.Show(this, "되돌리지 못했어요: " + ex.Message, "미니 치트"); }
        }
    }
}
