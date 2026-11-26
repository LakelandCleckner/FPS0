using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;

namespace Combat.Affixes.EditorTools
{
    // One-time move of armor stat perks to affixes: creates or updates the affixes, removes the armor slots' Stat columns,
    // clears the migrated perks from pools, and deletes them and any pools left empty.
    public static class AffixMigration
    {
        private const string LibraryPath = AffixText.Folder + "/AffixLibrary.asset";
        private const string StatPerkFolder = "Assets/GameData/Perks/Armor/Stat";
        private static readonly string[] RemovedColumns = { "Stat" };

        // Gun affixes keep their weapon settings and gain armor slots; on armor they boost every weapon.
        private const string Affixes =
            "Handling | handling flat 5,9,13,18,24 | weapons armor slots=arms\n" +
            "Reload Speed | reload_time pct -0.06,-0.1,-0.14,-0.2,-0.28 | weapons armor slots=arms\n" +
            "Aim Assist | aim_assist flat 5,8,11,15,20 | weapons armor slots=arms\n" +
            "Rifle Reload | reload_time pct -0.08,-0.12,-0.16,-0.2,-0.25 | armor slots=arms boosts=assault_rifle\n" +
            "SMG Reload | reload_time pct -0.08,-0.12,-0.16,-0.2,-0.25 | armor slots=arms boosts=submachine_gun\n" +
            "Shotgun Reload | reload_time pct -0.08,-0.12,-0.16,-0.2,-0.25 | armor slots=arms boosts=shotgun\n" +
            "Sidearm Reload | reload_time pct -0.08,-0.12,-0.16,-0.2,-0.25 | armor slots=arms boosts=sidearm\n" +
            "Move Speed | move_speed pct 0.02,0.03,0.05,0.07,0.1 | armor slots=legs player\n" +
            "Sprint Speed | sprint_multiplier pct 0.03,0.05,0.07,0.09,0.12 | armor slots=legs player\n" +
            "Jump | jump_force pct 0.03,0.05,0.07,0.09,0.12 | armor slots=legs player\n" +
            "Max Health | max_health flat lvl 25 | armor group=defense player\n" +
            "Max Health Percent | max_health pct lvl 0.03 | armor group=defense player\n" +
            "Damage Reduction | damage_taken flat -0.02,-0.03,-0.04,-0.06,-0.08 | armor group=defense player slots=chest,head\n" +
            "Fire Resistance | fire_resistance flat 0.05,0.08,0.11,0.15,0.2 | armor group=defense player slots=head\n" +
            "Frost Resistance | cold_resistance flat 0.05,0.08,0.11,0.15,0.2 | armor group=defense player slots=head\n" +
            "Shock Resistance | lightning_resistance flat 0.05,0.08,0.11,0.15,0.2 | armor group=defense player slots=head\n" +
            "Toxic Resistance | poison_resistance flat 0.05,0.08,0.11,0.15,0.2 | armor group=defense player slots=head\n" +
            "Ballistic Resistance | physical_resistance flat 0.03,0.05,0.07,0.09,0.12 | armor group=defense player slots=chest,head\n" +
            "Weapon Damage | weapon_damage pct lvl 0.08 | weapons armor damage slots=trinket,arms\n" +
            "Crit Chance | crit_chance flat lvl 0.01 | weapons armor damage player held slots=trinket,arms\n" +
            "Crit Damage | crit_damage flat lvl 0.06 | weapons armor damage player held slots=trinket,arms";

        [MenuItem("FPS0/Items & Perks/Migrate Armor Stat Perks to Affixes", priority = 533)]
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
                if (p != null) perks.Add(p);
            }
            var slots = AffixText.All<ArmorSlotSO>();
            int columns = slots.Sum(t => t.perkColumns.Count(c => c != null && RemovedColumns.Contains(c.label)));

            if (!EditorUtility.DisplayDialog("Affix migration",
                    $"Create or update {parsed.Count} affixes, remove {columns} armor Stat columns, and delete {perks.Count} armor stat perks " +
                    "plus any pools left empty.\n\nAsset deletes can't be undone; git has the old files.",
                    "Migrate", "Cancel")) return;

            var log = new List<string>();
            AffixText.Apply(parsed, library, log);
            RemoveColumns(slots, log);
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

        private static void RemoveColumns(List<ArmorSlotSO> slots, List<string> log)
        {
            foreach (var t in slots)
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

        // Pools this migration emptied, repeated so pools that only included emptied pools go too.
        private static void DeleteEmptyPools(List<PerkPoolSO> pools, List<PerkPoolSO> touched, List<string> log)
        {
            var deleted = new List<PerkPoolSO>();
            bool more = true;
            while (more)
            {
                more = false;
                foreach (var p in touched.ToList())
                {
                    if (p == null || deleted.Contains(p)) continue;
                    if (p.perks.Any(x => x != null) || p.includes.Any(x => x != null && !deleted.Contains(x))) continue;
                    deleted.Add(p);
                    more = true;
                    foreach (var other in pools)
                        if (other != null && !deleted.Contains(other) && other.includes.Contains(p))
                        {
                            Undo.RecordObject(other, "Drop empty include");
                            other.includes.Remove(p);
                            EditorUtility.SetDirty(other);
                            if (!touched.Contains(other)) touched.Add(other);
                        }
                }
            }
            foreach (var p in deleted)
            {
                string path = AssetDatabase.GetAssetPath(p);
                AssetDatabase.DeleteAsset(path);
                log.Add("Deleted empty pool " + path + ".");
            }
        }
    }
}
