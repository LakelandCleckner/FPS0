using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Attributes.EditorTools
{
    // Setup problems in an attribute library, each as one readable line.
    public static class AttributeValidation
    {
        public static List<string> Problems(AttributeLibrarySO library)
        {
            var problems = new List<string>();
            if (library == null) return problems;

            var ids = new HashSet<string>();
            foreach (var a in library.attributes)
            {
                if (a == null) { problems.Add("Empty slot in the attribute list."); continue; }
                if (a.stat == null) { problems.Add($"{a.name} has no stat."); continue; }
                if (!ids.Add(a.stat.id)) problems.Add($"Stat id '{a.stat.id}' is used by more than one attribute.");

                foreach (var f in a.feeds)
                {
                    if (f == null || f.stat == null) problems.Add($"{a.DisplayName} has a feed with no stat.");
                    else if (Mathf.Approximately(f.perPoint, 0f)) problems.Add($"{a.DisplayName} feeds {f.stat.id} with 0 per point.");
                }
            }

            foreach (var arch in LoadAll<WeaponArchetypeSO>())
            {
                int n = 0;
                foreach (var a in library.attributes) if (a != null && a.archetypes.Contains(arch)) n++;
                if (n == 0) problems.Add($"Archetype {Name(arch)} isn't linked to an attribute.");
                else if (n > 1) problems.Add($"Archetype {Name(arch)} is linked to {n} attributes.");
            }

            foreach (var type in LoadAll<DamageTypeSO>())
            {
                int n = 0;
                foreach (var a in library.attributes) if (a != null && a.element == type) n++;
                if (n == 0) problems.Add($"Element {type.displayName} isn't linked to an attribute.");
                else if (n > 1) problems.Add($"Element {type.displayName} is linked to {n} attributes.");
            }

            var registered = RegisteredStats(out bool anyBootstrap);
            if (anyBootstrap)
                foreach (var a in library.attributes)
                    if (a != null && a.stat != null && !registered.Contains(a.stat))
                        problems.Add($"{a.DisplayName}'s stat isn't in the open scene's StatBootstrap.");

            return problems;
        }

        // Stats listed on every StatBootstrap in the loaded scenes.
        public static HashSet<StatDefinitionSO> RegisteredStats(out bool anyBootstrap)
        {
            var set = new HashSet<StatDefinitionSO>();
            anyBootstrap = false;
            foreach (var boot in Object.FindObjectsByType<StatBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!boot.gameObject.scene.IsValid()) continue;
                anyBootstrap = true;
                var list = new SerializedObject(boot).FindProperty("statDefinitions");
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue is StatDefinitionSO s) set.Add(s);
            }
            return set;
        }

        public static List<T> LoadAll<T>() where T : Object
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        private static string Name(WeaponArchetypeSO a) => string.IsNullOrWhiteSpace(a.displayName) ? a.name : a.displayName;
    }

    [CustomEditor(typeof(AttributeLibrarySO))]
    public class AttributeLibrarySOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var problems = AttributeValidation.Problems((AttributeLibrarySO)target);
            if (problems.Count == 0) EditorGUILayout.HelpBox("Every archetype and element is linked to exactly one attribute.", MessageType.Info);
            foreach (var p in problems) EditorGUILayout.HelpBox(p, MessageType.Warning);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
