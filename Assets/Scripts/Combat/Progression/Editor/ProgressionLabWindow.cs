using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Enemies;
using Combat.Loadout;
using Combat.Loot;
using Combat.Perks;
using Combat.Stats;

namespace Combat.Progression.EditorTools
{
    // Edits the item and tier curves, previews item level against tier, enemies at a tier and loot level rolls,
    // and drives the tier and held item level in play mode.
    public class ProgressionLabWindow : EditorWindow
    {
        private const string ItemPath = "Assets/GameData/Progression/ItemScaling.asset";
        private const string TierPath = "Assets/GameData/Progression/TierScaling.asset";

        private static readonly string[] Tabs = { "Curves & Matchup", "Enemies", "Loot", "Live" };
        private static readonly Color Good = new Color(0.35f, 0.75f, 0.4f, 1f);
        private static readonly Color Warn = new Color(0.9f, 0.75f, 0.3f, 1f);
        private static readonly Color Bad = new Color(0.9f, 0.35f, 0.3f, 1f);

        private int tab;
        private Vector2 scroll;

        private ItemScalingSO item;
        private TierScalingSO tierCurve;
        private SerializedObject itemSo, tierSo;

        // Matchup
        private int tierFrom = 1, tierTo = 50, tierStep = 5;
        private int levelFrom = 1, levelTo = 50, levelStep = 5;
        private bool showDamage;
        private bool withAffixes;

        // Enemies
        private EnemyVariantSO variant;
        private EnemyArchetypeSO archetype;
        private int rankOverride = -1;
        private int previewTier = 10;

        // Loot
        private LootTableSO table;
        private int simTier = 10;
        private int simRolls = 200;
        private readonly SortedDictionary<int, int> simLevels = new SortedDictionary<int, int>();
        private readonly Dictionary<Rarity, int> simRarity = new Dictionary<Rarity, int>();

        [MenuItem("FPS0/Progression/Progression Lab", priority = 710)]
        private static void Open() => GetWindow<ProgressionLabWindow>("Progression Lab");

        private void OnEnable() => Load();
        private void OnFocus() => Load();
        private void OnInspectorUpdate() { if (Application.isPlaying && tab == 3) Repaint(); }

        private void Load()
        {
            item = ItemScaling.Active != null ? ItemScaling.Active : AssetDatabase.LoadAssetAtPath<ItemScalingSO>(ItemPath);
            tierCurve = Tier.Scaling != null ? Tier.Scaling : AssetDatabase.LoadAssetAtPath<TierScalingSO>(TierPath);
            itemSo = item != null ? new SerializedObject(item) : null;
            tierSo = tierCurve != null ? new SerializedObject(tierCurve) : null;
        }

        private void OnGUI()
        {
            if (item == null || tierCurve == null)
            {
                EditorGUILayout.HelpBox("Item or tier scaling asset missing. Run FPS0/Progression/Setup Item Level and Setup Tier Scaling.", MessageType.Warning);
                if (GUILayout.Button("Reload")) Load();
                return;
            }

            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: CurvesTab(); break;
                case 1: EnemiesTab(); break;
                case 2: LootTab(); break;
                case 3: LiveTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ curves and matchup

        private void CurvesTab()
        {
            EditorGUILayout.LabelField("Curves", EditorStyles.boldLabel);
            itemSo.Update();
            tierSo.Update();
            EditorGUILayout.PropertyField(itemSo.FindProperty("growth"), new GUIContent("Item growth / level"));
            EditorGUILayout.PropertyField(itemSo.FindProperty("maxLevel"), new GUIContent("Item max level"));
            EditorGUILayout.PropertyField(tierSo.FindProperty("healthGrowth"), new GUIContent("Enemy health growth / tier"));
            EditorGUILayout.PropertyField(tierSo.FindProperty("damageGrowth"), new GUIContent("Enemy damage growth / tier"));
            itemSo.ApplyModifiedProperties();
            tierSo.ApplyModifiedProperties();

            EditorGUILayout.HelpBox(
                $"2x damage = {Doubling(item.growth)} item levels.  2x enemy health = {Doubling(tierCurve.healthGrowth)} tiers.  " +
                $"2x enemy damage = {Doubling(tierCurve.damageGrowth)} tiers.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Matchup", EditorStyles.boldLabel);
            Range("Tiers", ref tierFrom, ref tierTo, ref tierStep);
            Range("Item levels", ref levelFrom, ref levelTo, ref levelStep);
            showDamage = GUILayout.Toolbar(showDamage ? 1 : 0, new[] { "Time to kill", "Damage taken" }) == 1;
            if (!showDamage)
                withAffixes = EditorGUILayout.ToggleLeft("With the best Weapon Damage affix (Mythic, greater; crit and precision left out)", withAffixes);

            if (showDamage)
                EditorGUILayout.HelpBox("Enemy hit damage relative to tier 1. Player health doesn't scale yet, so this is the raw factor.", MessageType.None);
            else
                Legend();

            MatchupGrid();
        }

        private static string Doubling(float growth) => growth > 0f ? (Mathf.Log(2f) / Mathf.Log(1f + growth)).ToString("0.#") : "never";

        private static void Range(string label, ref int from, ref int to, ref int step)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                from = Mathf.Max(1, EditorGUILayout.IntField(from, GUILayout.Width(50)));
                GUILayout.Label("to", GUILayout.Width(18));
                to = Mathf.Max(from, EditorGUILayout.IntField(to, GUILayout.Width(50)));
                GUILayout.Label("step", GUILayout.Width(30));
                step = Mathf.Max(1, EditorGUILayout.IntField(step, GUILayout.Width(40)));
            }
        }

        private static void Legend()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Time to kill vs. tuned (1.0 = item level equals tier):");
                Swatch(Good, "under 1.25x");
                Swatch(Warn, "1.25x to 2x");
                Swatch(Bad, "over 2x");
                GUILayout.FlexibleSpace();
            }
        }

        private static void Swatch(Color c, string text)
        {
            var r = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12), GUILayout.Height(12));
            r.y += 3;
            EditorGUI.DrawRect(r, c);
            GUILayout.Label(text, GUILayout.ExpandWidth(false));
        }

        private void MatchupGrid()
        {
            const float w = 52f;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(showDamage ? "Tier" : "Tier \\ Lv", EditorStyles.miniBoldLabel, GUILayout.Width(w));
                if (showDamage) GUILayout.Label("x dmg", EditorStyles.miniBoldLabel, GUILayout.Width(w));
                else for (int l = levelFrom; l <= levelTo; l += levelStep) GUILayout.Label("Lv " + l, EditorStyles.miniBoldLabel, GUILayout.Width(w));
            }

            for (int t = tierFrom; t <= tierTo; t += tierStep)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("T " + t, EditorStyles.miniBoldLabel, GUILayout.Width(w));
                    if (showDamage)
                    {
                        GUILayout.Label("x" + Short(tierCurve.DamageFactor(t)), GUILayout.Width(w));
                        continue;
                    }
                    for (int l = levelFrom; l <= levelTo; l += levelStep)
                    {
                        float ttk = tierCurve.HealthFactor(t) / (item.Factor(l) * (withAffixes ? AffixFactor(l) : 1f));
                        var r = GUILayoutUtility.GetRect(w, 18, GUILayout.Width(w));
                        var c = ttk < 1.25f ? Good : ttk <= 2f ? Warn : Bad;
                        EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(c.r, c.g, c.b, 0.35f));
                        GUI.Label(r, Short(ttk), EditorStyles.centeredGreyMiniLabel);
                    }
                }
            }
        }

        // Best Weapon Damage affix at an item level, greater.
        private static float AffixFactor(int level)
        {
            var lib = Combat.Affixes.AffixLibrary.Active;
            if (lib == null) return 1f;
            float weapon = 0f;
            foreach (var a in lib.affixes)
                if (a != null && a.stat != null && a.group == Combat.Affixes.AffixGroup.Damage && a.onWeapons && a.stat.id == "weapon_damage")
                    weapon = Mathf.Max(weapon, a.Value(Rarity.Mythic, level) * lib.greaterMultiplier);
            return 1f + weapon;
        }

        private static string Short(float v)
        {
            if (v >= 1000000f) return (v / 1000000f).ToString("0.#") + "m";
            if (v >= 1000f) return (v / 1000f).ToString("0.#") + "k";
            if (v >= 100f) return v.ToString("0");
            if (v >= 10f) return v.ToString("0.#");
            return v.ToString("0.##");
        }

        // ------------------------------------------------------------------ enemies

        private void EnemiesTab()
        {
            variant = (EnemyVariantSO)EditorGUILayout.ObjectField("Variant", variant, typeof(EnemyVariantSO), false);
            using (new EditorGUI.DisabledScope(variant != null))
                archetype = (EnemyArchetypeSO)EditorGUILayout.ObjectField("Archetype", archetype, typeof(EnemyArchetypeSO), false);

            var names = new List<string> { "As authored" };
            names.AddRange(EnemyTierLayers.RankNames);
            rankOverride = EditorGUILayout.Popup("Rank", rankOverride + 1, names.ToArray()) - 1;
            previewTier = Mathf.Max(1, EditorGUILayout.IntField("Tier", previewTier));

            var arch = variant != null ? variant.archetype : archetype;
            if (arch == null) { EditorGUILayout.HelpBox("Pick a variant or archetype.", MessageType.None); return; }
            if (!StatBootstrap.EnsureRegistry()) { EditorGUILayout.HelpBox("Open a scene with a StatBootstrap to resolve stats.", MessageType.Warning); return; }

            var layers = new List<EnemyModifierSO>();
            if (variant != null) foreach (var m in variant.modifiers) if (m != null) layers.Add(m);
            if (rankOverride >= 0) EnemyTierLayers.SetTier(layers, EnemyTierLayers.For((EnemyRank)rankOverride));

            var c = new StatContainer();
            var shown = new List<StatDefinitionSO>();
            foreach (var b in arch.baseStats)
                if (b.stat != null) { c.SetBase(b.stat, b.baseValue); Track(shown, b.stat); }
            foreach (var m in layers)
                foreach (var e in m.stats)
                {
                    if (e.stat == null) continue;
                    if (e.op == EnemyStatOp.SetBase) c.SetBase(e.stat, e.value);
                    else e.AddTo(c, m);
                    Track(shown, e.stat);
                }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{EnemySpawnDef.NameOf(arch, layers, variant != null ? variant.displayNameOverride : null)} " +
                                       $"({EnemySpawnDef.RankOf(arch, layers)})", EditorStyles.boldLabel);
            if (shown.Count == 0) EditorGUILayout.HelpBox("This definition sets no health or damage stats; the prefab's own values apply.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Stat", EditorStyles.miniBoldLabel, GUILayout.Width(180));
                GUILayout.Label("Tier 1", EditorStyles.miniBoldLabel, GUILayout.Width(90));
                GUILayout.Label("Tier " + previewTier, EditorStyles.miniBoldLabel, GUILayout.Width(90));
            }
            foreach (var s in shown)
            {
                float v = c.Resolve(s);
                float f = s == tierCurve.healthStat ? tierCurve.HealthFactor(previewTier)
                        : s.id.Contains("damage") ? tierCurve.DamageFactor(previewTier) : 1f;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(string.IsNullOrEmpty(s.displayName) ? s.id : s.displayName, GUILayout.Width(180));
                    GUILayout.Label(Short(v), GUILayout.Width(90));
                    GUILayout.Label(Short(v * f), GUILayout.Width(90));
                }
            }
            EditorGUILayout.HelpBox($"Tier {previewTier}: health x{Short(tierCurve.HealthFactor(previewTier))}, " +
                                    $"damage x{Short(tierCurve.DamageFactor(previewTier))} (applied through the enemy damage formula).", MessageType.None);
        }

        // Health and damage stats only.
        private static void Track(List<StatDefinitionSO> list, StatDefinitionSO s)
        {
            if (list.Contains(s) || string.IsNullOrEmpty(s.id)) return;
            if (s.id.Contains("health") || s.id.Contains("damage")) list.Add(s);
        }

        // ------------------------------------------------------------------ loot

        private void LootTab()
        {
            table = (LootTableSO)EditorGUILayout.ObjectField("Loot table", table, typeof(LootTableSO), false);
            using (new EditorGUI.DisabledScope(table != null && table.tier > 0))
                simTier = Mathf.Max(1, EditorGUILayout.IntField(table != null && table.tier > 0 ? "Tier (table's own)" : "Tier", table != null && table.tier > 0 ? table.tier : simTier));
            simRolls = Mathf.Clamp(EditorGUILayout.IntField("Rolls", simRolls), 1, 100000);

            using (new EditorGUI.DisabledScope(table == null))
                if (GUILayout.Button("Roll")) Simulate();

            if (simLevels.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Item level", EditorStyles.boldLabel);
            int most = 1;
            foreach (var kv in simLevels) most = Mathf.Max(most, kv.Value);
            foreach (var kv in simLevels) Bar("Lv " + kv.Key, kv.Value, most);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rarity", EditorStyles.boldLabel);
            most = 1;
            foreach (var kv in simRarity) most = Mathf.Max(most, kv.Value);
            foreach (Rarity r in System.Enum.GetValues(typeof(Rarity)))
                if (simRarity.TryGetValue(r, out int n)) Bar(r.ToString(), n, most);
        }

        private void Simulate()
        {
            simLevels.Clear();
            simRarity.Clear();
            int atTier = table.tier > 0 ? table.tier : simTier;
            for (int i = 0; i < simRolls; i++)
            {
                var r = table.rarity.Roll();
                int lv = table.RollLevel(r, atTier);
                simLevels[lv] = simLevels.TryGetValue(lv, out int a) ? a + 1 : 1;
                simRarity[r] = simRarity.TryGetValue(r, out int b) ? b + 1 : 1;
            }
        }

        private void Bar(string label, int count, int most)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(80));
                var r = GUILayoutUtility.GetRect(100, 16, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(new Rect(r.x, r.y + 2, r.width * count / most, r.height - 4), new Color(0.4f, 0.6f, 0.9f, 0.6f));
                GUILayout.Label($"{count} ({100f * count / simRolls:0.#}%)", GUILayout.Width(90));
            }
        }

        // ------------------------------------------------------------------ live

        private void LiveTab()
        {
            if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter play mode to change the tier and held item level.", MessageType.None); return; }
            int step = Event.current.shift ? 10 : 1;
            var keys = Object.FindFirstObjectByType<LoadoutDebugKeys>();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Tier");
                if (GUILayout.Button("-", GUILayout.Width(24))) SetTier(Tier.Current - step, keys);
                int t = EditorGUILayout.DelayedIntField(Tier.Current, GUILayout.Width(60));
                if (t != Tier.Current) SetTier(t, keys);
                if (GUILayout.Button("+", GUILayout.Width(24))) SetTier(Tier.Current + step, keys);
                GUILayout.Label($"health x{Short(tierCurve.HealthFactor(Tier.Current))}, damage x{Short(tierCurve.DamageFactor(Tier.Current))}");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Held item level");
                using (new EditorGUI.DisabledScope(keys == null))
                {
                    if (GUILayout.Button("-", GUILayout.Width(24))) keys.ChangeHeldLevel(-step);
                    if (GUILayout.Button("+", GUILayout.Width(24))) keys.ChangeHeldLevel(step);
                }
                if (keys == null) GUILayout.Label("No LoadoutDebugKeys in the scene.");
            }
            EditorGUILayout.HelpBox("Shift-click for steps of 10.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Live enemies ({EnemyDefinitionApplier.Active.Count})", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Enemy", EditorStyles.miniBoldLabel, GUILayout.Width(200));
                GUILayout.Label("Health", EditorStyles.miniBoldLabel, GUILayout.Width(120));
                GUILayout.Label("Tier damage", EditorStyles.miniBoldLabel, GUILayout.Width(90));
            }
            foreach (var e in EnemyDefinitionApplier.Active)
            {
                if (e == null || e.Stats == null) continue;
                var c = e.Stats.Container;
                string hp = e.Health != null ? $"{Short(e.Health.CurrentHealth)} / {Short(e.Health.MaxHealth)}" : "-";
                string dmg = c != null && tierCurve.damageStat != null ? "x" + Short(c.Resolve(tierCurve.damageStat)) : "-";
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(e.DisplayName, EditorStyles.label, GUILayout.Width(200))) EditorGUIUtility.PingObject(e.gameObject);
                    GUILayout.Label(hp, GUILayout.Width(120));
                    GUILayout.Label(dmg, GUILayout.Width(90));
                }
            }
        }

        private static void SetTier(int tier, LoadoutDebugKeys keys)
        {
            if (keys != null) keys.ChangeTier(Mathf.Max(1, tier) - Tier.Current);
            else Tier.Set(tier);
        }
    }
}
