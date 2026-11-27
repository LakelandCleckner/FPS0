using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Stats;
using Combat.Stats.EditorTools;

namespace Combat.Affixes.EditorTools
{
    // Creates the affix library if missing and assigns it to every StatBootstrap. Safe to run again.
    public static class AffixSetup
    {
        private const string Folder = "Assets/GameData/Affixes";
        private const string LibraryPath = Folder + "/AffixLibrary.asset";

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
