using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Loadout;
using Combat.Stats;
using Combat.Stats.EditorTools;

namespace Combat.Affixes.EditorTools
{
    // Creates the affix library with three sample weapon affixes and assigns it to every StatBootstrap. Safe to run again.
    public static class AffixSetup
    {
        private const string Folder = "Assets/GameData/Affixes";
        private const string LibraryPath = Folder + "/AffixLibrary.asset";

        private struct Sample
        {
            public string id, name, stat;
            public ModifierBucket bucket;
            public float[] values;
        }

        private static readonly Sample[] Samples =
        {
            new Sample { id = "range_flat", name = "Range", stat = "weapon_range", bucket = ModifierBucket.Flat, values = new[] { 4f, 6f, 8f, 10f, 12f } },
            new Sample { id = "handling_flat", name = "Handling", stat = "handling", bucket = ModifierBucket.Flat, values = new[] { 4f, 6f, 8f, 10f, 12f } },
            new Sample { id = "reload_pct", name = "Reload Speed", stat = "reload_time", bucket = ModifierBucket.Additive, values = new[] { -0.04f, -0.06f, -0.08f, -0.1f, -0.12f } },
        };

        [MenuItem("FPS0/Items & Perks/Setup Affixes", priority = 530)]
        private static void Run()
        {
            var log = new List<string>();
            BootstrapScenes.EnsureFolder(Folder);

            var library = AssetDatabase.LoadAssetAtPath<AffixLibrarySO>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AffixLibrarySO>();
                AssetDatabase.CreateAsset(library, LibraryPath);
                log.Add("Created " + LibraryPath + ".");
            }

            var stats = new Dictionary<string, StatDefinitionSO>();
            foreach (var g in AssetDatabase.FindAssets("t:StatDefinitionSO"))
            {
                var st = AssetDatabase.LoadAssetAtPath<StatDefinitionSO>(AssetDatabase.GUIDToAssetPath(g));
                if (st != null && !string.IsNullOrEmpty(st.id) && !stats.ContainsKey(st.id)) stats[st.id] = st;
            }

            foreach (var s in Samples)
            {
                string path = Folder + "/" + s.name + ".asset";
                var affix = AssetDatabase.LoadAssetAtPath<AffixSO>(path);
                if (affix == null)
                {
                    if (!stats.TryGetValue(s.stat, out var stat)) { log.Add($"No stat '{s.stat}': skipped {s.name}."); continue; }
                    affix = ScriptableObject.CreateInstance<AffixSO>();
                    affix.id = s.id;
                    affix.displayName = s.name;
                    affix.stat = stat;
                    affix.bucket = s.bucket;
                    affix.target = ModifierTarget.Weapons;
                    affix.rarityValues = s.values;
                    affix.onWeapons = true;
                    AssetDatabase.CreateAsset(affix, path);
                    log.Add("Created affix " + s.name + ".");
                }
                if (!library.affixes.Contains(affix))
                {
                    Undo.RecordObject(library, "Add affix");
                    library.affixes.Add(affix);
                    EditorUtility.SetDirty(library);
                }
            }

            BootstrapScenes.ForEach(scene =>
            {
                bool changed = false;
                foreach (var boot in BootstrapScenes.InScene<StatBootstrap>(scene))
                {
                    var so = new SerializedObject(boot);
                    var prop = so.FindProperty("affixLibrary");
                    if (prop == null || prop.objectReferenceValue == library) continue;
                    prop.objectReferenceValue = library;
                    so.ApplyModifiedProperties();
                    changed = true;
                    log.Add($"Assigned the affix library on {boot.name} ({scene.name}).");
                }
                return changed;
            }, log);

            AssetDatabase.SaveAssets();
            AffixLibrary.Active = library;
            if (log.Count == 0) log.Add("Already set up.");
            Debug.Log("[AffixSetup] Done.\n" + string.Join("\n", log));
        }
    }
}
