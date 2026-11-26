using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Combat.Perks;
using Combat.Progression;

namespace Combat.Affixes.EditorTools
{
    // Adds the starter damage affixes and the armor element affixes, moves Precision Damage into the damage group, sets the item curve to +6% per level,
    // and deletes weapon pools the affix migration left empty. Safe to run again.
    public static class DamageAffixSetup
    {
        private const string LibraryPath = AffixText.Folder + "/AffixLibrary.asset";
        private const string ItemScalingPath = "Assets/GameData/Progression/ItemScaling.asset";
        private const string WeaponPools = "Assets/GameData/Perks/Pools/Weapon";
        private const float ItemGrowth = 0.06f;

        private const string Affixes =
            "Weapon Damage | weapon_damage pct lvl 0.08 | weapons damage\n" +
            "Precision Damage | precision_damage pct lvl 0.06 | weapons damage\n" +
            "Crit Chance | crit_chance flat lvl 0.01 | weapons damage player held\n" +
            "Crit Damage | crit_damage flat lvl 0.06 | weapons damage player held\n" +
            "Fire Damage | fire_damage pct lvl 0.08 | armor group=element player element=fire\n" +
            "Frost Damage | frost_damage pct lvl 0.08 | armor group=element player element=ice\n" +
            "Shock Damage | shock_damage pct lvl 0.08 | armor group=element player element=lightning\n" +
            "Toxic Damage | toxic_damage pct lvl 0.08 | armor group=element player element=poison\n" +
            "Ballistic Damage | ballistic_damage pct lvl 0.08 | armor group=element player element=physical";

        [MenuItem("FPS0/Items & Perks/Setup Damage Affixes", priority = 532)]
        private static void Run()
        {
            var library = AssetDatabase.LoadAssetAtPath<AffixLibrarySO>(LibraryPath);
            if (library == null) { Debug.LogError("[DamageAffixSetup] No affix library. Run Setup Affixes first."); return; }

            var errors = new List<string>();
            var parsed = AffixText.Parse(Affixes, errors);
            if (errors.Count > 0) { Debug.LogError("[DamageAffixSetup] " + string.Join("\n", errors)); return; }

            var log = new List<string>();
            AffixText.Apply(parsed, library, log);

            var scaling = AssetDatabase.LoadAssetAtPath<ItemScalingSO>(ItemScalingPath);
            if (scaling != null && !Mathf.Approximately(scaling.growth, ItemGrowth))
            {
                Undo.RecordObject(scaling, "Item growth");
                scaling.growth = ItemGrowth;
                EditorUtility.SetDirty(scaling);
                log.Add($"Item growth set to +{ItemGrowth * 100f:0}% per level.");
            }

            DeleteUnusedEmptyPools(log);
            AssetDatabase.SaveAssets();
            Debug.Log("[DamageAffixSetup] Done.\n" + string.Join("\n", log));
        }

        // Weapon pools with no perks or includes that no other asset references.
        private static void DeleteUnusedEmptyPools(List<string> log)
        {
            var texts = new List<string>();
            foreach (var f in Directory.GetFiles("Assets/GameData", "*.asset", SearchOption.AllDirectories)) texts.Add(File.ReadAllText(f));

            foreach (var g in AssetDatabase.FindAssets("t:PerkPoolSO", new[] { WeaponPools }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var pool = AssetDatabase.LoadAssetAtPath<PerkPoolSO>(path);
                if (pool == null || pool.perks.Exists(p => p != null) || pool.includes.Exists(i => i != null)) continue;
                if (texts.Exists(t => t.Contains(g))) continue;
                AssetDatabase.DeleteAsset(path);
                log.Add("Deleted unused empty pool " + path + ".");
            }
        }
    }
}
