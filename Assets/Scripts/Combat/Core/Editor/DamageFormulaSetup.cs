using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Combat.Enemies;
using Combat.Stats;
using Combat.Stats.EditorTools;
using Combat.UI;

namespace Combat.Core.EditorTools
{
    // Creates the element damage stats and the player and enemy damage formulas, links each damage type to its
    // stat, registers the stats, assigns a formula to every resolver without one, and adds the Character tab group.
    // Safe to run again: existing assets and assignments are kept.
    public static class DamageFormulaSetup
    {
        private const string StatFolder = "Assets/GameData/Stats/Definitions/Elements";
        private const string FormulaFolder = "Assets/GameData/Damage";
        private const string PlayerPath = FormulaFolder + "/PlayerDamageFormula.asset";
        private const string EnemyPath = FormulaFolder + "/EnemyDamageFormula.asset";
        private const string SheetGroup = "Element Damage";

        // Damage type id, stat id, stat name
        private static readonly (string type, string id, string name)[] Elements =
        {
            ("fire", "fire_damage", "Fire Damage"),
            ("ice", "frost_damage", "Frost Damage"),
            ("lightning", "shock_damage", "Shock Damage"),
            ("poison", "toxic_damage", "Toxic Damage"),
            ("physical", "ballistic_damage", "Ballistic Damage"),
        };

        [MenuItem("FPS0/Stats/Setup Damage Formula", priority = 651)]
        private static void Run()
        {
            BootstrapScenes.EnsureFolder(StatFolder);
            BootstrapScenes.EnsureFolder(FormulaFolder);
            var log = new List<string>();

            var stats = ElementStats(log);
            var player = Formula(PlayerPath, log);
            var enemy = Formula(EnemyPath, log);

            AssignPrefabs(player, enemy, log);
            BootstrapScenes.ForEach(scene =>
            {
                bool changed = BootstrapScenes.Register(scene, stats, log);
                changed |= AssignScene(scene, player, enemy, log);
                return changed;
            }, log);
            AddSheetGroup(stats, log);
            AssetDatabase.SaveAssets();

            foreach (var p in player.Problems()) log.Add("Check player formula: " + p);
            foreach (var p in enemy.Problems()) log.Add("Check enemy formula: " + p);
            Selection.activeObject = player;
            Debug.Log("[DamageFormulaSetup] Done.\n" + string.Join("\n", log));
        }

        // One damage stat per element, linked from its damage type.
        private static List<StatDefinitionSO> ElementStats(List<string> log)
        {
            var byId = new Dictionary<string, StatDefinitionSO>();
            foreach (var g in AssetDatabase.FindAssets("t:StatDefinitionSO"))
            {
                var s = AssetDatabase.LoadAssetAtPath<StatDefinitionSO>(AssetDatabase.GUIDToAssetPath(g));
                if (s != null && !string.IsNullOrEmpty(s.id) && !byId.ContainsKey(s.id)) byId[s.id] = s;
            }
            var types = new Dictionary<string, DamageTypeSO>();
            foreach (var g in AssetDatabase.FindAssets("t:DamageTypeSO"))
            {
                var t = AssetDatabase.LoadAssetAtPath<DamageTypeSO>(AssetDatabase.GUIDToAssetPath(g));
                if (t != null && !string.IsNullOrEmpty(t.id) && !types.ContainsKey(t.id)) types[t.id] = t;
            }

            var list = new List<StatDefinitionSO>();
            foreach (var e in Elements)
            {
                if (!byId.TryGetValue(e.id, out var stat))
                {
                    stat = ScriptableObject.CreateInstance<StatDefinitionSO>();
                    stat.id = e.id;
                    stat.displayName = e.name;
                    stat.resolutionMode = StatResolutionMode.ScaledBase;
                    stat.defaultValue = 1f;
                    stat.useMin = true;
                    stat.minValue = 0f;
                    stat.displayDecimals = 2;
                    AssetDatabase.CreateAsset(stat, $"{StatFolder}/{e.name}.asset");
                    log.Add($"Created stat {e.id}.");
                }
                list.Add(stat);

                if (!types.TryGetValue(e.type, out var type)) { log.Add($"No damage type with id '{e.type}'."); continue; }
                if (type.damageStat != null) continue;
                Undo.RecordObject(type, "Link element damage stat");
                type.damageStat = stat;
                EditorUtility.SetDirty(type);
                log.Add($"Linked {type.name} to {e.id}.");
            }
            return list;
        }

        private static DamageFormulaSO Formula(string path, List<string> log)
        {
            var f = AssetDatabase.LoadAssetAtPath<DamageFormulaSO>(path);
            if (f != null) return f;
            f = ScriptableObject.CreateInstance<DamageFormulaSO>();
            f.layers = DamageFormulaSO.DefaultLayers();
            AssetDatabase.CreateAsset(f, path);
            log.Add($"Created {f.name}.");
            return f;
        }

        // Base prefabs first so variants inherit instead of overriding.
        private static void AssignPrefabs(DamageFormulaSO player, DamageFormulaSO enemy, List<string> log)
        {
            var bases = new List<string>();
            var variants = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<WeaponHitResolver>(true) == null) continue;
                (PrefabUtility.GetPrefabAssetType(go) == PrefabAssetType.Variant ? variants : bases).Add(path);
            }

            foreach (var path in bases) AssignPrefab(path, player, enemy, log);
            foreach (var path in variants) AssignPrefab(path, player, enemy, log);
        }

        private static void AssignPrefab(string path, DamageFormulaSO player, DamageFormulaSO enemy, List<string> log)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool needed = false;
            foreach (var r in asset.GetComponentsInChildren<WeaponHitResolver>(true))
                if (r.Formula == null) needed = true;
            if (!needed) return;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = AssignAll(root, root.GetComponentsInChildren<WeaponHitResolver>(true), player, enemy);
                if (n > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.Add($"Assigned a formula to {n} resolver(s) in {asset.name}.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool AssignScene(Scene scene, DamageFormulaSO player, DamageFormulaSO enemy, List<string> log)
        {
            int n = 0;
            foreach (var r in BootstrapScenes.InScene<WeaponHitResolver>(scene))
                n += AssignAll(r.transform.root.gameObject, new[] { r }, player, enemy);
            if (n > 0) log.Add($"Assigned a formula to {n} resolver(s) in {scene.name}.");
            return n > 0;
        }

        // Enemy formula under an enemy (one with an enemy attack), player formula otherwise.
        private static int AssignAll(GameObject root, WeaponHitResolver[] resolvers, DamageFormulaSO player, DamageFormulaSO enemy)
        {
            bool isEnemy = root.GetComponentInChildren<EnemyMeleeAttack>(true) != null
                        || root.GetComponentInChildren<EnemyRangedAttack>(true) != null;
            int n = 0;
            foreach (var r in resolvers)
            {
                var so = new SerializedObject(r);
                var prop = so.FindProperty("formula");
                if (prop == null || prop.objectReferenceValue != null) continue;
                prop.objectReferenceValue = isEnemy ? enemy : player;
                so.ApplyModifiedProperties();
                n++;
            }
            return n;
        }

        // An Element Damage group on the Character tab, after Attributes when present.
        private static void AddSheetGroup(List<StatDefinitionSO> defs, List<string> log)
        {
            StatSheetSO sheet = null;
            foreach (var g in AssetDatabase.FindAssets("t:StatSheetSO"))
            {
                var s = AssetDatabase.LoadAssetAtPath<StatSheetSO>(AssetDatabase.GUIDToAssetPath(g));
                if (s != null && s.name == "CharacterStatSheet") { sheet = s; break; }
            }
            if (sheet == null) { log.Add("No CharacterStatSheet asset found: Character tab group not added."); return; }

            int at = 0;
            for (int i = 0; i < sheet.groups.Count; i++)
            {
                var g = sheet.groups[i];
                if (g == null) continue;
                if (g.label == SheetGroup) return;
                if (g.label == "Attributes") at = i + 1;
            }

            var group = new StatSheetSO.Group { label = SheetGroup };
            foreach (var d in defs)
                if (d != null) group.entries.Add(new StatSheetSO.Entry { stat = d, display = StatDisplay.Number, decimals = 2, unit = "x" });

            Undo.RecordObject(sheet, "Add Element Damage group");
            sheet.groups.Insert(at, group);
            EditorUtility.SetDirty(sheet);
            log.Add("Added the Element Damage group to the Character tab.");
        }
    }
}
