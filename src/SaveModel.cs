using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MiniCheat
{
    // 세이브 하나를 열어서 값 읽기·바꾸기·저장 (게임기 모드 화면이 씀)
    class SaveModel
    {
        public GameData Db;
        public SaveSlot Slot;
        public JObj Root;
        public bool Dirty;
        SaveFmt fmt;
        RubySave rsave;
        LsdSave lsd;
        DateTime loaded;

        public static SaveModel Open(GameData db, SaveSlot s)
        {
            var m = new SaveModel { Db = db, Slot = s };
            if (db.IsRuby) { m.rsave = RubySave.Load(s.Path, db.Kind); m.Root = m.rsave.Root; }
            else if (db.Kind == RpgKind.R2000) { m.lsd = LsdSave.Load(s.Path, db); m.Root = m.lsd.Root; }
            else m.Root = Json.Parse(SaveCodec.Read(s.Path, out m.fmt)) as JObj;
            if (m.Root == null) throw new Exception("세이브 모양이 예상과 달라요");
            m.loaded = File.GetLastWriteTimeUtc(s.Path);
            return m;
        }

        public bool ChangedOnDisk { get { return File.Exists(Slot.Path) && File.GetLastWriteTimeUtc(Slot.Path) != loaded; } }

        public void Save()
        {
            Backup.Make(Db.BackupName, Slot.Path, null);
            if (rsave != null) rsave.Save(Slot.Path);
            else if (lsd != null) lsd.Save(Slot.Path);
            else SaveCodec.Write(Slot.Path, Json.Write(Root), fmt);
            loaded = File.GetLastWriteTimeUtc(Slot.Path);
            Dirty = false;
        }

        void Mark() { Dirty = true; }

        // ---------- 돈·기타 ----------
        public JObj Party { get { return Json.Obj(Root, "party"); } }
        public double Gold { get { return Json.Num(Party, "_gold"); } set { Json.SetNum(Party, "_gold", value); Mark(); } }
        public double MaxGold { get { return Db.Kind == RpgKind.R2000 ? 999999 : 99999999; } }
        public double Steps { get { return Json.Num(Party, "_steps"); } set { Json.SetNum(Party, "_steps", value); Mark(); } }

        int Fps { get { return Db.Kind == RpgKind.XP ? 40 : 60; } }

        public double PlaySeconds
        {
            get
            {
                double f = Json.Get(Root, "frameCount") != null ? Json.Num(Root, "frameCount") : Json.Num(Json.Get(Root, "system"), "_framesOnSave");
                return Math.Floor(f / Fps);
            }
            set
            {
                if (Json.Get(Root, "frameCount") != null) Root["frameCount"] = JVal.N(value * Fps);
                else Json.SetNum(Json.Get(Root, "system"), "_framesOnSave", value * Fps);
                Mark();
            }
        }

        // ---------- 캐릭터 ----------
        public List<int> PartyIds()
        {
            var pa = Json.Arr(Party, "_actors");
            return pa == null ? new List<int>() : pa.Items.Select(n => n is JObj ? (int)Json.Num(n, "_actorId") : (int)Json.Num(n)).ToList();
        }

        public List<JObj> Actors()
        {
            var data = Json.Arr(Json.Get(Root, "actors"), "_data");
            if (data == null) return new List<JObj>();
            var ids = PartyIds();
            return data.Items.OfType<JObj>().OrderBy(a => { int i = ids.IndexOf(Id(a)); return i < 0 ? 1000 : i; }).ToList();
        }

        public int Id(JObj a) { return (int)Json.Num(a, "_actorId"); }
        public bool InParty(JObj a) { return PartyIds().Contains(Id(a)); }
        public string Name(JObj a) { return Db.T(Json.Str(a, "_name")); }
        public int ClassId(JObj a) { return (int)Json.Num(a, "_classId", Db.Kind == RpgKind.R2000 ? 0 : 1); }
        public string ClassName(JObj a) { return Db.ClassName(ClassId(a)); }

        public int Level(JObj a) { return (int)Json.Num(a, "_level", 1); }
        public int MaxLevel(JObj a) { return Math.Max(Db.MaxLevel(Id(a)), Level(a)); }

        public void SetLevel(JObj a, int lv)
        {
            a["_level"] = JVal.N(lv);
            double need = Db.ExpFor(Id(a), ClassId(a), lv);
            var exp = Json.Obj(a, "_exp");
            if (exp != null) exp[ClassId(a).ToString()] = JVal.N(need);
            else if (Json.Get(a, "_exp") is JVal) a["_exp"] = JVal.N(Math.Max(need, Db.Kind == RpgKind.R2000 ? need : 0));
            Mark();
        }

        public double Hp(JObj a) { return Json.Num(a, "_hp"); }
        public double Mp(JObj a) { return Json.Num(a, Db.MpKey); }
        public void SetHp(JObj a, double v) { a["_hp"] = JVal.N(v); Mark(); }
        public void SetMp(JObj a, double v) { a[Db.MpKey] = JVal.N(v); Mark(); }

        public int Base(JObj a, int i) { return Db.BaseParam(Id(a), ClassId(a), i, Level(a)); }

        public double Plus(JObj a, int i)
        {
            var keys = Db.PlusKeys;
            return keys != null ? Json.Num(a, keys[i]) : Json.Num(Json.At(Json.Arr(a, "_paramPlus"), i));
        }

        public void SetPlus(JObj a, int i, double v)
        {
            var keys = Db.PlusKeys;
            if (keys != null) a[keys[i]] = JVal.N(v);
            else
            {
                var plus = Json.Arr(a, "_paramPlus");
                if (plus == null) { plus = new JArr(); a["_paramPlus"] = plus; }
                Json.SetAt(plus, i, JVal.N(v));
            }
            Mark();
        }

        public double Total(JObj a, int i) { return Base(a, i) + Plus(a, i); }

        // ---------- 장비 ----------
        public class EquipSlot { public int EType; public DbItem Cur; public Action<DbItem> Set; }

        public List<EquipSlot> Equips(JObj a)
        {
            var list = new List<EquipSlot>();
            if (Db.Kind == RpgKind.VX || Db.Kind == RpgKind.XP || Db.Kind == RpgKind.R2000)
            {
                string[] keys = { "_weaponId", "_armor1Id", "_armor2Id", "_armor3Id", "_armor4Id" };
                for (int i = 0; i < keys.Length; i++)
                {
                    if (Json.Get(a, keys[i]) == null) continue;
                    string k = keys[i];
                    DbItem cur;
                    (i == 0 ? Db.Weapons : Db.Armors).TryGetValue((int)Json.Num(a, k), out cur);
                    list.Add(new EquipSlot { EType = i + 1, Cur = cur, Set = it => { a[k] = JVal.N(it == null ? 0 : it.Id); Mark(); } });
                }
                return list;
            }
            var eq = Json.Arr(a, "_equips");
            if (eq == null) return list;
            bool ace = Db.Kind == RpgKind.VXAce;
            for (int i = 0; i < eq.Items.Count; i++)
            {
                var gi = eq.Items[i] as JObj;
                string c = Json.Str(gi, ace ? "_class" : "_dataClass");
                string cls = ace ? (c.EndsWith("RPG::Weapon") ? "weapon" : c.EndsWith("RPG::Armor") ? "armor" : "") : c;
                int et = i == 1 && cls == "weapon" ? 1 : i + 1;
                DbItem cur = null;
                int id = (int)Json.Num(gi, "_itemId");
                if (id > 0) (cls == "weapon" ? Db.Weapons : Db.Armors).TryGetValue(id, out cur);
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
                        Mark();
                    }
                });
            }
            return list;
        }

        public string EquipName(int etype) { return etype < Db.EquipTypes.Length && Db.EquipTypes[etype].Length > 0 ? Db.EquipTypes[etype] : "장비"; }

        public IEnumerable<DbItem> EquipChoices(int etype) { return etype == 1 ? Db.Weapons.Values : Db.Armors.Values.Where(x => x.EType == etype); }

        // ---------- 스킬 ----------
        public List<int> Skills(JObj a)
        {
            var sk = Json.Arr(a, "_skills");
            return sk == null ? new List<int>() : sk.Items.Select(n => (int)Json.Num(n)).ToList();
        }

        public void AddSkill(JObj a, int id)
        {
            var sk = Json.Arr(a, "_skills");
            if (sk == null) { sk = new JArr(); a["_skills"] = sk; }
            if (sk.Items.Any(n => (int)Json.Num(n) == id)) return;
            sk.Items.Add(JVal.N(id));
            var sorted = sk.Items.OrderBy(n => Json.Num(n)).ToList();
            sk.Items.Clear(); sk.Items.AddRange(sorted);
            Mark();
        }

        public void RemoveSkill(JObj a, int id)
        {
            var sk = Json.Arr(a, "_skills");
            if (sk == null) return;
            sk.Items.RemoveAll(n => (int)Json.Num(n) == id);
            Mark();
        }

        // ---------- 가방 ----------
        JObj Bag(DbItem it)
        {
            string k = Db.Kind == RpgKind.R2000 ? "_items" : it.Kind == 1 ? "_weapons" : it.Kind == 2 ? "_armors" : "_items";
            var o = Json.Obj(Party, k);
            if (o == null && Party != null) { o = new JObj(); Party[k] = o; }
            return o;
        }

        public int Count(DbItem it) { return (int)Json.Num(Json.Get(Bag(it), it.Id.ToString())); }

        public void SetCount(DbItem it, int n)
        {
            var c = Bag(it);
            if (c == null) return;
            if (n <= 0) c.Remove(it.Id.ToString()); else c[it.Id.ToString()] = JVal.N(n);
            Mark();
        }

        public int MaxCount { get { return 99; } }

        // cat: 0 아이템, 1 무기, 2 방어구, 3 중요 아이템
        public List<DbItem> BagItems(int cat, bool all, string find)
        {
            var src = cat == 1 ? Db.Weapons : cat == 2 ? Db.Armors : Db.Items;
            var list = src.Values.Where(x => cat == 3 ? x.IType == 2 : cat == 0 ? x.IType != 2 : true).ToList();
            find = (find ?? "").Trim();
            return list.Where(it => (all || Count(it) > 0) && (find.Length == 0 || it.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0))
                       .OrderBy(x => x.Id).ToList();
        }

        // ---------- 스위치·변수 ----------
        public JArr Switches { get { return Json.Arr(Json.Get(Root, "switches"), "_data"); } }
        public JArr Variables { get { return Json.Arr(Json.Get(Root, "variables"), "_data"); } }

        public int FlagCount(bool sw)
        {
            var names = sw ? Db.SwitchNames : Db.VarNames;
            var d = sw ? Switches : Variables;
            return Math.Max(names.Length, d == null ? 0 : d.Items.Count);
        }

        public string FlagName(bool sw, int i) { var n = sw ? Db.SwitchNames : Db.VarNames; return i < n.Length ? n[i] : ""; }
        public bool GetSwitch(int i) { return Json.Bool(Json.At(Switches, i)); }
        public void SetSwitch(int i, bool v) { if (Switches != null) { Json.SetAt(Switches, i, JVal.B(v)); Mark(); } }
        public JVal VarCell(int i) { return Json.At(Variables, i) as JVal; }
        public bool VarIsNumber(int i) { var v = Json.At(Variables, i); return v == null || (v is JVal && (((JVal)v).IsNum || ((JVal)v).Raw == "null")); }
        public double GetVar(int i) { return Json.Num(Json.At(Variables, i)); }
        public void SetVar(int i, double v) { if (Variables != null) { Json.SetAt(Variables, i, JVal.N(v)); Mark(); } }
    }
}
