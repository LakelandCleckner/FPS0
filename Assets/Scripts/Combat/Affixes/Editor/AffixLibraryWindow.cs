using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Affixes.EditorTools
{
    // Edits the affix library, imports affixes from text, simulates rolls, and checks each archetype's best roll against stat caps.
    public class AffixLibraryWindow : EditorWindow
    {
        private const string LibraryPath = AffixText.Folder + "/AffixLibrary.asset";
        private static readonly string[] Tabs = { "Library", "Import", "Roll", "Best Roll" };
        private static readonly Color Over = new Color(0.9f, 0.35f, 0.3f, 1f);

        private int tab;
        private Vector2 scroll, listScroll;
        private AffixLibrarySO library;
        private SerializedObject librarySo;
        private AffixSO selected;
        private Editor selectedEditor;

        // Import
        [SerializeField] private string importText = "";
        private readonly List<string> importErrors = new List<string>();
        private List<AffixText.Parsed> importParsed = new List<AffixText.Parsed>();

        // Roll
        private WeaponSO rollWeapon;
        private ArmorSO rollArmor;
        private Rarity rollRarity = Rarity.Rare;
        private int rollCount = 500;
        private readonly Dictionary<AffixSO, int> rolled = new Dictionary<AffixSO, int>();
        private readonly Dictionary<AffixSO, int> rolledGreater = new Dictionary<AffixSO, int>();
        private int rolledItems;

        // Best roll
        private bool bestGreater = true;
        private bool onlyCapped;
        private readonly List<BestRow> best = new List<BestRow>();

        private struct BestRow
        {
            public string Archetype, Stat, Affixes;
            public float Base, Best, Cap;
            public bool Capped;
        }

        [MenuItem("FPS0/Items & Perks/Affix Library", priority = 529)]
        private static void Open() => GetWindow<AffixLibraryWindow>("Affix Library");

        private void OnEnable() => Load();
        private void OnFocus() { if (library == null) Load(); }
        private void OnDisable() { if (selectedEditor != null) DestroyImmediate(selectedEditor); }

        private void Load()
        {
            library = AffixLibrary.Active != null ? AffixLibrary.Active : AssetDatabase.LoadAssetAtPath<AffixLibrarySO>(LibraryPath);
            librarySo = library != null ? new SerializedObject(library) : null;
        }

        private void OnGUI()
        {
            if (library == null)
            {
                EditorGUILayout.HelpBox("No affix library. Run FPS0/Items & Perks/Setup Affixes.", MessageType.Warning);
                if (GUILayout.Button("Reload")) Load();
                return;
            }
            tab = GUILayout.Toolbar(tab, Tabs);
            switch (tab)
            {
                case 0: LibraryTab(); break;
                case 1: ImportTab(); break;
                case 2: RollTab(); break;
                case 3: BestTab(); break;
            }
        }

        // ------------------------------------------------------------------ library

        private void LibraryTab()
        {
            librarySo.Update();
            EditorGUILayout.PropertyField(librarySo.FindProperty("countByRarity"), new GUIContent("Stat affixes per rarity"), true);
            EditorGUILayout.PropertyField(librarySo.FindProperty("damageCountByRarity"), new GUIContent("Damage affixes per rarity"), true);
            EditorGUILayout.PropertyField(librarySo.FindProperty("elementCountByRarity"), new GUIContent("Element affixes per rarity"), true);
            EditorGUILayout.PropertyField(librarySo.FindProperty("levelRampAtMax"), new GUIContent("Level ramp at max level"));
            EditorGUILayout.PropertyField(librarySo.FindProperty("greaterChance"));
            EditorGUILayout.PropertyField(librarySo.FindProperty("greaterMultiplier"));
            librarySo.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(240)))
                {
                    listScroll = EditorGUILayout.BeginScrollView(listScroll);
                    foreach (var a in library.affixes.ToList())
                    {
                        if (a == null) continue;
                        var style = a == selected ? EditorStyles.boldLabel : EditorStyles.label;
                        string where = (a.group == AffixGroup.Damage ? "Dmg " : a.group == AffixGroup.Element ? "Elem " : "Stat ") + (a.onWeapons ? "W" : "") + (a.onArmor ? "A" : "");
                        if (GUILayout.Button($"{a.DisplayName}   [{where}]", style)) Select(a);
                    }
                    EditorGUILayout.EndScrollView();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("New")) NewAffix();
                        using (new EditorGUI.DisabledScope(selected == null))
                        {
                            if (GUILayout.Button("Remove")) RemoveSelected(false);
                            if (GUILayout.Button("Delete")) RemoveSelected(true);
                        }
                    }
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    if (selected == null) EditorGUILayout.HelpBox("Pick an affix to edit it.", MessageType.None);
                    else
                    {
                        if (selectedEditor == null || selectedEditor.target != selected) Editor.CreateCachedEditor(selected, null, ref selectedEditor);
                        selectedEditor.OnInspectorGUI();
                        EditorGUILayout.Space();
                        EditorGUILayout.LabelField("Values", EditorStyles.boldLabel);
                        if (selected.valueSource == AffixValueSource.ItemLevel)
                            foreach (int lv in new[] { 1, 25, 50, 75, Combat.Progression.ItemScaling.MaxLevel })
                                EditorGUILayout.LabelField("Item level " + lv, $"{selected.Value(Rarity.Common, lv):0.###}   greater {selected.Value(Rarity.Common, lv) * library.greaterMultiplier:0.###}");
                        else
                            foreach (Rarity r in System.Enum.GetValues(typeof(Rarity)))
                                EditorGUILayout.LabelField(r.ToString(), $"{selected.Value(r, 1):0.###}   greater {selected.Value(r, 1) * library.greaterMultiplier:0.###}");
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void Select(AffixSO a)
        {
            selected = a;
            GUI.FocusControl(null);
        }

        private void NewAffix()
        {
            Combat.Stats.EditorTools.BootstrapScenes.EnsureFolder(AffixText.Folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(AffixText.Folder + "/New Affix.asset");
            var a = CreateInstance<AffixSO>();
            a.displayName = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(a, path);
            Undo.RecordObject(library, "Add affix");
            library.affixes.Add(a);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Select(a);
        }

        private void RemoveSelected(bool deleteAsset)
        {
            if (deleteAsset && !EditorUtility.DisplayDialog("Delete affix", $"Delete {selected.DisplayName}'s asset? This can't be undone.", "Delete", "Cancel")) return;
            Undo.RecordObject(library, "Remove affix");
            library.affixes.Remove(selected);
            EditorUtility.SetDirty(library);
            if (deleteAsset) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(selected));
            AssetDatabase.SaveAssets();
            selected = null;
        }

        // ------------------------------------------------------------------ import

        private void ImportTab()
        {
            EditorGUILayout.HelpBox(
                "Name | stat kind values | options\n" +
                "kind: flat, pct, mult.  values: c,u,r,l,m, or \"lvl 12\" for an item level base.\n" +
                "options: weapons, armor, types=a,b, archetypes=a,b, slots=a,b, weight=1, player, held, boosts=a,b\n" +
                "Existing affixes with the same name are updated.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load current")) { importText = AffixText.Write(library); Reparse(); }
                using (new EditorGUI.DisabledScope(importParsed.Count == 0 || importErrors.Count > 0))
                    if (GUILayout.Button("Apply")) ApplyImport();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            importText = EditorGUILayout.TextArea(importText, GUILayout.MinHeight(200));
            if (EditorGUI.EndChangeCheck()) Reparse();

            foreach (var e in importErrors) EditorGUILayout.HelpBox(e, MessageType.Error);
            if (importErrors.Count == 0 && importParsed.Count > 0)
                foreach (var p in importParsed)
                {
                    bool exists = AssetDatabase.LoadAssetAtPath<AffixSO>(AffixText.Folder + "/" + p.Name + ".asset") != null;
                    EditorGUILayout.LabelField((exists ? "update  " : "new     ") + p.Name, p.Stat.id);
                }
            EditorGUILayout.EndScrollView();
        }

        private void Reparse()
        {
            importErrors.Clear();
            importParsed = AffixText.Parse(importText, importErrors);
        }

        private void ApplyImport()
        {
            var log = new List<string>();
            AffixText.Apply(importParsed, library, log);
            Debug.Log("[AffixLibrary] Import done.\n" + string.Join("\n", log));
        }

        // ------------------------------------------------------------------ roll

        private void RollTab()
        {
            rollWeapon = (WeaponSO)EditorGUILayout.ObjectField("Weapon", rollWeapon, typeof(WeaponSO), false);
            rollArmor = (ArmorSO)EditorGUILayout.ObjectField("or armor", rollArmor, typeof(ArmorSO), false);
            rollRarity = (Rarity)EditorGUILayout.EnumPopup("Rarity", rollRarity);
            rollCount = Mathf.Clamp(EditorGUILayout.IntField("Items", rollCount), 1, 100000);
            using (new EditorGUI.DisabledScope(rollWeapon == null && rollArmor == null))
                if (GUILayout.Button("Roll")) Roll();

            if (rolledItems == 0) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField($"{rolledItems} items, {library.CountFor(AffixGroup.Stat, rollRarity)} stat and " +
                                       $"{library.CountFor(AffixGroup.Damage, rollRarity)} damage slots, {library.CountFor(AffixGroup.Element, rollRarity)} element slots", EditorStyles.boldLabel);
            int most = Mathf.Max(1, rolled.Values.DefaultIfEmpty(1).Max());
            AffixGroup? shown = null;
            foreach (var kv in rolled.OrderBy(k => k.Key.group).ThenByDescending(k => k.Value))
            {
                if (shown != kv.Key.group) { shown = kv.Key.group; EditorGUILayout.LabelField(shown.ToString(), EditorStyles.miniBoldLabel); }
                rolledGreater.TryGetValue(kv.Key, out int g);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(kv.Key.DisplayName, GUILayout.Width(150));
                    var r = GUILayoutUtility.GetRect(100, 16, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(new Rect(r.x, r.y + 2, r.width * kv.Value / most, r.height - 4), new Color(0.93f, 0.56f, 0.74f, 0.6f));
                    GUILayout.Label($"{100f * kv.Value / rolledItems:0.#}% of items, {g} greater", GUILayout.Width(170));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Roll()
        {
            rolled.Clear();
            rolledGreater.Clear();
            rolledItems = rollCount;
            var into = new List<AffixRoll>();
            for (int i = 0; i < rollCount; i++)
            {
                if (rollWeapon != null) library.Roll(rollWeapon, rollRarity, into);
                else library.Roll(rollArmor, rollRarity, into);
                foreach (var a in into)
                {
                    rolled[a.affix] = rolled.TryGetValue(a.affix, out int n) ? n + 1 : 1;
                    if (a.greater) rolledGreater[a.affix] = rolledGreater.TryGetValue(a.affix, out int g) ? g + 1 : 1;
                }
            }
        }

        // ------------------------------------------------------------------ best roll

        private void BestTab()
        {
            EditorGUILayout.HelpBox("Each archetype's base stats against its best possible Mythic affixes on that stat " +
                                    "(up to its group's Mythic slots), at max item level on its first weapon. Red = the stat hits its cap.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                bestGreater = EditorGUILayout.ToggleLeft("All greater", bestGreater, GUILayout.Width(100));
                onlyCapped = EditorGUILayout.ToggleLeft("Only capped", onlyCapped, GUILayout.Width(100));
                if (GUILayout.Button("Check")) CheckBest();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Archetype", EditorStyles.miniBoldLabel, GUILayout.Width(170));
                GUILayout.Label("Stat", EditorStyles.miniBoldLabel, GUILayout.Width(130));
                GUILayout.Label("Base", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                GUILayout.Label("Best", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                GUILayout.Label("Cap", EditorStyles.miniBoldLabel, GUILayout.Width(50));
                GUILayout.Label("Affixes", EditorStyles.miniBoldLabel);
            }
            foreach (var row in best)
            {
                if (onlyCapped && !row.Capped) continue;
                var r = EditorGUILayout.BeginHorizontal();
                if (row.Capped) EditorGUI.DrawRect(r, new Color(Over.r, Over.g, Over.b, 0.25f));
                GUILayout.Label(row.Archetype, GUILayout.Width(170));
                GUILayout.Label(row.Stat, GUILayout.Width(130));
                GUILayout.Label(row.Base.ToString("0.##"), GUILayout.Width(60));
                GUILayout.Label(row.Best.ToString("0.##"), GUILayout.Width(60));
                GUILayout.Label(float.IsNaN(row.Cap) ? "-" : row.Cap.ToString("0.##"), GUILayout.Width(50));
                GUILayout.Label(row.Affixes, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void CheckBest()
        {
            best.Clear();
            if (!StatBootstrap.EnsureRegistry()) { Debug.LogWarning("[AffixLibrary] Open a scene with a StatBootstrap first."); return; }
            var keys = AffixText.All<WeaponStatKeys>().FirstOrDefault();
            if (keys == null) { Debug.LogWarning("[AffixLibrary] No WeaponStatKeys asset."); return; }
            var weapons = AffixText.All<WeaponSO>();
            int level = Combat.Progression.ItemScaling.MaxLevel;

            foreach (var arch in AffixText.All<WeaponArchetypeSO>().OrderBy(a => a.name))
            {
                var weapon = weapons.FirstOrDefault(w => w.archetype == arch);
                if (weapon == null) continue;
                var eligible = library.affixes.Where(a => a != null && a.target == Combat.Loadout.ModifierTarget.Weapons && a.CanRollOn(arch)).ToList();

                foreach (var stat in eligible.Select(a => a.stat).Distinct())
                {
                    var baseC = new StatContainer();
                    WeaponStatBuilder.PopulateBases(baseC, weapon, keys, level);
                    float baseV = baseC.Resolve(stat);
                    var onStat = eligible.Where(a => a.stat == stat).ToList();
                    int slots = onStat.Max(a => library.CountFor(a.group, Rarity.Mythic));

                    // Strongest affixes on this stat, by how far each moves it alone.
                    var ranked = onStat
                        .Select(a => (a, gain: Mathf.Abs(Solo(weapon, keys, a, stat) - baseV)))
                        .OrderByDescending(x => x.gain).Take(slots).Select(x => x.a).ToList();

                    var c = new StatContainer();
                    WeaponStatBuilder.PopulateBases(c, weapon, keys, level);
                    foreach (var a in ranked) AddAffix(c, a);
                    float v = c.Resolve(stat);

                    float cap = float.NaN;
                    bool capped = false;
                    if (stat.useMax && v >= stat.maxValue - 0.0001f) { cap = stat.maxValue; capped = true; }
                    else if (stat.useMin && v <= stat.minValue + 0.0001f) { cap = stat.minValue; capped = true; }
                    else if (stat.useMax) cap = stat.maxValue;
                    else if (stat.useMin) cap = stat.minValue;

                    best.Add(new BestRow
                    {
                        Archetype = string.IsNullOrEmpty(arch.displayName) ? arch.name : arch.displayName,
                        Stat = string.IsNullOrEmpty(stat.displayName) ? stat.id : stat.displayName,
                        Base = baseV, Best = v, Cap = cap, Capped = capped,
                        Affixes = string.Join(", ", ranked.Select(a => a.DisplayName))
                    });
                }
            }
        }

        private float Solo(WeaponSO weapon, WeaponStatKeys keys, AffixSO a, StatDefinitionSO stat)
        {
            var c = new StatContainer();
            WeaponStatBuilder.PopulateBases(c, weapon, keys, Combat.Progression.ItemScaling.MaxLevel);
            AddAffix(c, a);
            return c.Resolve(stat);
        }

        private void AddAffix(StatContainer c, AffixSO a)
        {
            float v = a.Value(Rarity.Mythic, Combat.Progression.ItemScaling.MaxLevel);
            if (bestGreater) v *= library.greaterMultiplier;
            c.AddModifier(new StatModifier(a.stat, Combat.Loadout.ModifierGrant.IdOf(a.bucket), v, a.scope), a);
        }
    }
}
