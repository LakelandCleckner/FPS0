using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Stats;
using Combat.Stats.EditorTools;

namespace Combat.Progression.EditorTools
{
    // Creates the item scaling asset and assigns it to every StatBootstrap. Safe to run again.
    public static class ItemLevelSetup
    {
        private const string Folder = "Assets/GameData/Progression";
        private const string AssetPath = Folder + "/ItemScaling.asset";

        [MenuItem("FPS0/Progression/Setup Item Level", priority = 700)]
        private static void Run()
        {
            var log = new List<string>();
            BootstrapScenes.EnsureFolder(Folder);

            var scaling = AssetDatabase.LoadAssetAtPath<ItemScalingSO>(AssetPath);
            if (scaling == null)
            {
                scaling = ScriptableObject.CreateInstance<ItemScalingSO>();
                AssetDatabase.CreateAsset(scaling, AssetPath);
                AssetDatabase.SaveAssets();
                log.Add("Created " + AssetPath + ".");
            }

            BootstrapScenes.ForEach(scene =>
            {
                bool changed = false;
                foreach (var boot in BootstrapScenes.InScene<StatBootstrap>(scene))
                {
                    var so = new SerializedObject(boot);
                    var prop = so.FindProperty("itemScaling");
                    if (prop == null || prop.objectReferenceValue == scaling) continue;
                    prop.objectReferenceValue = scaling;
                    so.ApplyModifiedProperties();
                    changed = true;
                    log.Add($"Assigned item scaling on {boot.name} ({scene.name}).");
                }
                return changed;
            }, log);

            ItemScaling.Active = scaling;
            if (log.Count == 0) log.Add("Already set up.");
            Debug.Log("[ItemLevelSetup] Done.\n" + string.Join("\n", log));
        }
    }
}
