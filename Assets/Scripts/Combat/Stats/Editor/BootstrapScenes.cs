using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Combat.Stats.EditorTools
{
    // Editor helpers for every scene that holds a StatBootstrap.
    public static class BootstrapScenes
    {
        // Runs edit on each scene with a StatBootstrap, saves the ones it changed and closes the ones it opened.
        public static void ForEach(Func<Scene, bool> edit, List<string> log)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { log.Add("Scene update cancelled."); return; }

            string guid = ScriptGuid(typeof(StatBootstrap));
            if (string.IsNullOrEmpty(guid)) { log.Add("StatBootstrap script not found: scenes not updated."); return; }

            var loaded = new Dictionary<string, Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (sc.isLoaded && !string.IsNullOrEmpty(sc.path)) loaded[sc.path] = sc;
            }

            int found = 0;
            foreach (var sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(sceneGuid);
                if (!File.ReadAllText(path).Contains(guid)) continue;
                found++;

                bool opened = !loaded.TryGetValue(path, out var scene);
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                if (edit(scene)) EditorSceneManager.SaveScene(scene);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            if (found == 0) log.Add("No scene has a StatBootstrap.");
        }

        // Adds the stats to every StatBootstrap in the scene. True if anything was added.
        public static bool Register(Scene scene, IEnumerable<StatDefinitionSO> defs, List<string> log)
        {
            bool changed = false;
            foreach (var boot in InScene<StatBootstrap>(scene))
            {
                var so = new SerializedObject(boot);
                var list = so.FindProperty("statDefinitions");
                var have = new HashSet<UnityEngine.Object>();
                for (int i = 0; i < list.arraySize; i++) have.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

                int added = 0;
                foreach (var d in defs)
                {
                    if (d == null || have.Contains(d)) continue;
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = d;
                    have.Add(d);
                    added++;
                }
                if (added == 0) continue;
                so.ApplyModifiedProperties();
                changed = true;
                log.Add($"Registered {added} stats on {boot.name} ({scene.name}).");
            }
            return changed;
        }

        public static List<T> InScene<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        public static string ScriptGuid(Type type)
        {
            foreach (var g in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                if (script != null && script.GetClass() == type) return g;
            }
            return null;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
