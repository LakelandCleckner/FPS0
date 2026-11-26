using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Events;
using Combat.Loot;
using Combat.Stats;
using Combat.Stats.EditorTools;

namespace Combat.Progression.EditorTools
{
    // Creates the tier damage stat and tier scaling asset, registers and assigns them in every StatBootstrap scene,
    // adds the tier layer to the enemy damage formula, and points loot tables at the current tier. Safe to run again.
    public static class TierScalingSetup
    {
        private const string Folder = "Assets/GameData/Progression";
        private const string AssetPath = Folder + "/TierScaling.asset";
        private const string StatFolder = "Assets/GameData/Stats/Definitions/Progression";
        private const string StatPath = StatFolder + "/TierDamage.asset";
        private const string EnemyFormulaPath = "Assets/GameData/Damage/EnemyDamageFormula.asset";

        [MenuItem("FPS0/Progression/Setup Tier Scaling", priority = 701)]
        private static void Run()
        {
            var log = new List<string>();
            BootstrapScenes.EnsureFolder(Folder);
            BootstrapScenes.EnsureFolder(StatFolder);

            var damageStat = FindStat("tier_damage");
            if (damageStat == null)
            {
                damageStat = ScriptableObject.CreateInstance<StatDefinitionSO>();
                damageStat.id = "tier_damage";
                damageStat.displayName = "Tier Damage";
                damageStat.resolutionMode = StatResolutionMode.ScaledBase;
                damageStat.defaultValue = 1f;
                damageStat.useMin = true;
                damageStat.minValue = 0f;
                damageStat.displayDecimals = 2;
                AssetDatabase.CreateAsset(damageStat, StatPath);
                log.Add("Created stat tier_damage.");
            }
            var healthStat = FindStat("max_health");
            if (healthStat == null) log.Add("No stat with id 'max_health': enemy health won't scale.");

            var scaling = AssetDatabase.LoadAssetAtPath<TierScalingSO>(AssetPath);
            if (scaling == null)
            {
                scaling = ScriptableObject.CreateInstance<TierScalingSO>();
                AssetDatabase.CreateAsset(scaling, AssetPath);
                log.Add("Created " + AssetPath + ".");
            }
            if (scaling.damageStat == null || (scaling.healthStat == null && healthStat != null))
            {
                Undo.RecordObject(scaling, "Link tier stats");
                if (scaling.damageStat == null) scaling.damageStat = damageStat;
                if (scaling.healthStat == null) scaling.healthStat = healthStat;
                EditorUtility.SetDirty(scaling);
                log.Add("Linked tier scaling stats.");
            }

            BootstrapScenes.ForEach(scene =>
            {
                bool changed = BootstrapScenes.Register(scene, new[] { damageStat }, log);
                foreach (var boot in BootstrapScenes.InScene<StatBootstrap>(scene))
                {
                    var so = new SerializedObject(boot);
                    var prop = so.FindProperty("tierScaling");
                    if (prop == null || prop.objectReferenceValue == scaling) continue;
                    prop.objectReferenceValue = scaling;
                    so.ApplyModifiedProperties();
                    changed = true;
                    log.Add($"Assigned tier scaling on {boot.name} ({scene.name}).");
                }
                return changed;
            }, log);

            AddFormulaLayer(damageStat, log);
            LootToCurrentTier(log);

            AssetDatabase.SaveAssets();
            Tier.Scaling = scaling;
            if (log.Count == 0) log.Add("Already set up.");
            Debug.Log("[TierScalingSetup] Done.\n" + string.Join("\n", log));
        }

        private static StatDefinitionSO FindStat(string id)
        {
            foreach (var g in AssetDatabase.FindAssets("t:StatDefinitionSO"))
            {
                var s = AssetDatabase.LoadAssetAtPath<StatDefinitionSO>(AssetDatabase.GUIDToAssetPath(g));
                if (s != null && s.id == id) return s;
            }
            return null;
        }

        private static void AddFormulaLayer(StatDefinitionSO stat, List<string> log)
        {
            var formula = AssetDatabase.LoadAssetAtPath<DamageFormulaSO>(EnemyFormulaPath);
            if (formula == null) { log.Add("No enemy damage formula at " + EnemyFormulaPath + "."); return; }
            foreach (var l in formula.layers)
                if (l != null && l.layer == DamageLayer.Stat && l.stat == stat) return;

            Undo.RecordObject(formula, "Add tier layer");
            formula.layers.Add(new DamageFormulaLayer
            {
                layer = DamageLayer.Stat, appliesTo = HitSourceMask.All, label = "Tier",
                stat = stat, owner = StatScope.Attacker, mode = StatLayerMode.Value
            });
            EditorUtility.SetDirty(formula);
            log.Add("Added the Tier layer to the enemy damage formula.");
        }

        // Tier 1 was the old default; 0 now means the current tier.
        private static void LootToCurrentTier(List<string> log)
        {
            foreach (var g in AssetDatabase.FindAssets("t:LootTableSO"))
            {
                var t = AssetDatabase.LoadAssetAtPath<LootTableSO>(AssetDatabase.GUIDToAssetPath(g));
                if (t == null || t.tier != 1) continue;
                Undo.RecordObject(t, "Loot table current tier");
                t.tier = 0;
                EditorUtility.SetDirty(t);
                log.Add($"Loot table {t.name} now uses the current tier.");
            }
        }
    }
}
