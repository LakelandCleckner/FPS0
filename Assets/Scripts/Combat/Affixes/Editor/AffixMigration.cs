using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.Affixes.EditorTools
{
    // One-time move of weapon stat perks to affixes: creates the affixes, removes the Barrel and Magazine columns,
    // clears the migrated perks from pools, and deletes them and any pools left empty.
    public static class AffixMigration
    {
        private const string LibraryPath = AffixText.Folder + "/AffixLibrary.asset";
        private const string StatPerkFolder = "Assets/GameData/Perks/Weapon/Stat";
        private static readonly string[] RemovedColumns = { "Barrel", "Magazine" };

        // Intrinsics on unique armor; they stay perks.
        private static readonly string[] Kept = { "Ophidian Grip", "Chunker Plating" };

        private const string Affixes =
            "Range | weapon_range flat 5,8,12,16,20 | weapons\n" +
            "Stability | weapon_stability flat 4,7,10,13,16 | weapons\n" +
            "Handling | handling flat 5,9,13,18,24 | weapons\n" +
            "Recoil Control | weapon_recoil_direction flat 3,5,7,9,12 | weapons\n" +
            "Aim Assist | aim_assist flat 5,8,11,15,20 | weapons\n" +
            "Magazine | magazine_size pct 0.08,0.12,0.16,0.22,0.3 | weapons\n" +
            "Reload Speed | reload_time pct -0.06,-0.1,-0.14,-0.2,-0.28 | weapons\n" +
            "Reload Time | reload_time flat -0.05,-0.1,-0.15,-0.2,-0.25 | weapons\n" +
            "Pellet Spread | pellet_spread pct -0.05,-0.08,-0.12,-0.16,-0.2 | weapons types=shotgun archetypes=sidearm_scatter\n" +
            "Stagger | stagger_power pct 0.08,0.12,0.16,0.2,0.25 | weapons\n" +
            "Precision Damage | precision_damage pct 0.04,0.06,0.08,0.1,0.12 | weapons";

        [MenuItem("FPS0/Items & Perks/Migrate Weapon Stat Perks to Affixes", priority = 531)]
        private static void Run()
        {
            var library = AssetDatabase.LoadAssetAtPath<AffixLibrarySO>(LibraryPath);
            if (library == null) { EditorUtility.DisplayDialog("Affix migration", "No affix library. Run Setup Affixes first.", "OK"); return; }

            var errors = new List<string>();
            var parsed = AffixText.Parse(Affixes, errors);
            if (errors.Count > 0) { Debug.LogError("[AffixMigration] " + string.Join("\n", errors)); return; }

            var perks = new List<PerkSO>();
            foreach (var g in AssetDatabase.FindAssets("t:PerkSO", new[] { StatPerkFolder }))
            {
                var p = AssetDatabase.LoadAssetAtPath<PerkSO>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && !Kept.Contains(p.name)) perks.Add(p);
            }
            var types = AffixText.All<WeaponTypeSO>();
            int columns = types.Sum(t => t.perkColumns.Count(c => c != null && RemovedColumns.Contains(c.label)));

            if (!EditorUtility.DisplayDialog("Affix migration",
                    $"Create or update {parsed.Count} affixes, remove {columns} Barrel/Magazine columns, and delete {perks.Count} weapon stat perks " +
                    $"(Ophidian Grip and Chunker Plating stay) plus any pools left empty.\n\nAsset deletes can't be undone; git has the old files.",
                    "Migrate", "Cancel")) return;

            var log = new List<string>();
            AffixText.Apply(parsed, library, log);
            RemoveColumns(types, log);
            var pools = AffixText.All<PerkPoolSO>();
            var touched = ClearFromPools(pools, perks, log);

            foreach (var p in perks)
            {
                string path = AssetDatabase.GetAssetPath(p);
                AssetDatabase.DeleteAsset(path);
                log.Add("Deleted " + path + ".");
            }
            DeleteEmptyPools(pools, touched, log);

            AssetDatabase.SaveAssets();
            Debug.Log("[AffixMigration] Done.\n" + string.Join("\n", log));
        }

        private static void RemoveColumns(List<WeaponTypeSO> types, List<string> log)
        {
            foreach (var t in types)
            {
                int before = t.perkColumns.Count;
                Undo.RecordObject(t, "Remove stat columns");
                t.perkColumns.RemoveAll(c => c != null && RemovedColumns.Contains(c.label));
                if (t.perkColumns.Count == before) continue;
                EditorUtility.SetDirty(t);
                log.Add($"Removed {before - t.perkColumns.Count} columns from {t.name}.");
            }
        }

        // Returns the pools it changed.
        private static List<PerkPoolSO> ClearFromPools(List<PerkPoolSO> pools, List<PerkSO> perks, List<string> log)
        {
            var touched = new List<PerkPoolSO>();
            foreach (var pool in pools)
            {
                int before = pool.perks.Count;
                if (!pool.perks.Any(p => perks.Contains(p))) continue;
                Undo.RecordObject(pool, "Clear stat perks");
                pool.perks.RemoveAll(p => perks.Contains(p));
                EditorUtility.SetDirty(pool);
                log.Add($"Removed {before - pool.perks.Count} perks from pool {pool.name}.");
                touched.Add(pool);
            }
            return touched;
        }

        // Pools this migration emptied (no perks, no includes); dropped from other pools' includes first.
        private static void DeleteEmptyPools(List<PerkPoolSO> pools, List<PerkPoolSO> touched, List<string> log)
        {
            var empty = touched.Where(p => p != null && p.perks.Count(x => x != null) == 0 && p.includes.Count(x => x != null) == 0).ToList();
            if (empty.Count == 0) return;
            foreach (var pool in pools)
            {
                if (pool == null || empty.Contains(pool) || !pool.includes.Any(i => empty.Contains(i))) continue;
                Undo.RecordObject(pool, "Drop empty includes");
                pool.includes.RemoveAll(i => empty.Contains(i));
                EditorUtility.SetDirty(pool);
            }
            foreach (var p in empty)
            {
                string path = AssetDatabase.GetAssetPath(p);
                AssetDatabase.DeleteAsset(path);
                log.Add("Deleted empty pool " + path + ".");
            }
        }
    }
}
