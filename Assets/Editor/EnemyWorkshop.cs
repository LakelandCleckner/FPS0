using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Enemies;

// One window for enemy definitions: browse every archetype, modifier and hand-made variant, and find broken or duplicate definitions.
public class EnemyWorkshop : EditorWindow
{
    private enum Tab { Browse, Cleanup }
    private enum Filter { All, Archetypes, TierModifiers, ElementModifiers, OtherModifiers, Variants }

    private static readonly string[] TabNames = { "Browse", "Cleanup" };

    private Tab tab;
    private readonly List<EnemyArchetypeSO> archetypes = new List<EnemyArchetypeSO>();
    private readonly List<EnemyModifierSO> modifiers = new List<EnemyModifierSO>();
    private readonly List<EnemyVariantSO> variants = new List<EnemyVariantSO>();

    // Browse
    private Filter filter;
    private EnemyArchetypeSO archFilter;
    private string search = "";
    private Object selected;
    private Editor selectedEditor;
    private Vector2 listScroll, inspectorScroll;

    // Cleanup
    private Vector2 cleanupScroll;

    // True while the workshop draws an asset inspector, so it hides its own "Open in Workshop" button.
    public static bool Drawing { get; private set; }

    [MenuItem("FPS0/Enemies/Workshop", priority = 0)]
    public static void Open() => Open(null);

    public static void Open(Object select)
    {
        var w = GetWindow<EnemyWorkshop>("Enemy Workshop");
        w.minSize = new Vector2(720f, 420f);
        w.Refresh();
        if (select != null)
        {
            w.tab = Tab.Browse;
            w.filter = Filter.All;
            w.search = "";
            w.Select(select);
        }
    }

    private void OnEnable() => Refresh();
    private void OnProjectChange() { Refresh(); Repaint(); }
    private void OnDisable() { if (selectedEditor != null) DestroyImmediate(selectedEditor); }

    private void Refresh()
    {
        Load(archetypes);
        Load(modifiers);
        Load(variants);
        if (selected == null && selectedEditor != null) { DestroyImmediate(selectedEditor); selectedEditor = null; }
    }

    private static void Load<T>(List<T> list) where T : Object
    {
        list.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) list.Add(a);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames, EditorStyles.toolbarButton, GUILayout.Width(220f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f))) Refresh();
        }

        switch (tab)
        {
            case Tab.Browse: DrawBrowse(); break;
            case Tab.Cleanup: DrawCleanup(); break;
        }
    }

    // ------------------------------------------------------------------ browse

    private void DrawBrowse()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(260f)))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                filter = (Filter)EditorGUILayout.EnumPopup(filter);
                if (filter == Filter.Variants || filter == Filter.All)
                    archFilter = (EnemyArchetypeSO)EditorGUILayout.ObjectField(archFilter, typeof(EnemyArchetypeSO), false);

                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                foreach (var o in Entries()) Row(o);
                EditorGUILayout.EndScrollView();
            }

            using (new EditorGUILayout.VerticalScope())
            {
                if (selected == null)
                {
                    EditorGUILayout.HelpBox("Pick an archetype, modifier or variant on the left.", MessageType.Info);
                    return;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(selected.name, EditorStyles.boldLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(50f))) EditorGUIUtility.PingObject(selected);
                }
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                Editor.CreateCachedEditor(selected, null, ref selectedEditor);
                Drawing = true;
                try { selectedEditor.OnInspectorGUI(); }
                finally { Drawing = false; }
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private IEnumerable<Object> Entries()
    {
        bool Match(Object o) => string.IsNullOrEmpty(search) || o.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;

        if (filter == Filter.All || filter == Filter.Archetypes)
            foreach (var a in archetypes) if (Match(a)) yield return a;

        foreach (var m in modifiers)
        {
            bool show = filter == Filter.All
                || (filter == Filter.TierModifiers && m.kind == EnemyModifierKind.Tier)
                || (filter == Filter.ElementModifiers && m.kind == EnemyModifierKind.Element)
                || (filter == Filter.OtherModifiers && m.kind == EnemyModifierKind.Other);
            if (show && Match(m)) yield return m;
        }

        if (filter == Filter.All || filter == Filter.Variants)
            foreach (var v in variants)
                if ((archFilter == null || v.archetype == archFilter) && Match(v)) yield return v;
    }

    private void Row(Object o)
    {
        string tag = o is EnemyArchetypeSO ? "A" : o is EnemyModifierSO m ? KindTag(m.kind) : "V";
        var style = o == selected ? EditorStyles.whiteLabel : EditorStyles.label;
        var rect = EditorGUILayout.GetControlRect();
        if (o == selected) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.5f));
        GUI.Label(new Rect(rect.x, rect.y, 22f, rect.height), tag, EditorStyles.miniBoldLabel);
        GUI.Label(new Rect(rect.x + 22f, rect.y, rect.width - 22f, rect.height), o.name, style);
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            Select(o);
            if (Event.current.clickCount == 2) EditorGUIUtility.PingObject(o);
            Event.current.Use();
        }
    }

    private static string KindTag(EnemyModifierKind k) => k == EnemyModifierKind.Tier ? "T" : k == EnemyModifierKind.Element ? "E" : "M";

    private void Select(Object o)
    {
        if (selected == o) return;
        selected = o;
        inspectorScroll = Vector2.zero;
        Repaint();
    }

    // ------------------------------------------------------------------ cleanup

    private class Problem
    {
        public Object Asset;
        public string Text;
        public string FixLabel;
        public System.Action Fix;
    }

    private void DrawCleanup()
    {
        var problems = FindProblems();
        cleanupScroll = EditorGUILayout.BeginScrollView(cleanupScroll);
        if (problems.Count == 0) EditorGUILayout.HelpBox("No problems found.", MessageType.Info);

        foreach (var p in problems)
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(p.Text, EditorStyles.wordWrappedLabel);
                if (p.Asset != null && GUILayout.Button("Ping", GUILayout.Width(50f))) EditorGUIUtility.PingObject(p.Asset);
                if (p.Fix != null && GUILayout.Button(p.FixLabel, GUILayout.Width(110f)))
                {
                    var fix = p.Fix;
                    EditorApplication.delayCall += () => { fix(); Refresh(); Repaint(); };
                }
            }
        EditorGUILayout.EndScrollView();
    }

    private List<Problem> FindProblems()
    {
        var list = new List<Problem>();

        foreach (var a in archetypes)
        {
            if (a.bodyPrefab == null)
                list.Add(new Problem { Asset = a, Text = $"{a.name}: no body prefab." });
            else if (a.bodyPrefab.GetComponent<EnemyDefinitionApplier>() == null)
            {
                var arch = a;
                list.Add(new Problem { Asset = a, Text = $"{a.name}: body prefab has no EnemyDefinitionApplier.",
                                       FixLabel = "Add applier", Fix = () => EnemySetupFixes.AddApplier(arch.bodyPrefab, arch) });
            }
            else if (a.bodyPrefab.GetComponents<EnemyDefinitionApplier>().Length > 1)
                list.Add(new Problem { Asset = a.bodyPrefab, Text = $"{a.bodyPrefab.name}: more than one EnemyDefinitionApplier." });
        }

        foreach (var v in variants)
        {
            if (v.archetype == null)
                list.Add(new Problem { Asset = v, Text = $"{v.name}: no archetype." });

            if (v.modifiers.Any(m => m == null))
            {
                var variant = v;
                list.Add(new Problem { Asset = v, Text = $"{v.name}: empty or missing modifier slot.",
                                       FixLabel = "Remove empty", Fix = () => RemoveEmpty(variant) });
            }

            if (string.IsNullOrEmpty(v.id))
                list.Add(new Problem { Asset = v, Text = $"{v.name}: no id (the spawner falls back to the asset name)." });
        }

        foreach (var g in variants.Where(v => !string.IsNullOrEmpty(v.id)).GroupBy(v => v.id).Where(g => g.Count() > 1))
            list.Add(new Problem { Asset = g.First(), Text = $"Duplicate id '{g.Key}': {string.Join(", ", g.Select(v => v.name))}." });

        // Same archetype and same modifier set means two assets spawn the same enemy.
        var seen = new Dictionary<string, EnemyVariantSO>();
        foreach (var v in variants)
        {
            if (v.archetype == null) continue;
            var ids = v.modifiers.Where(m => m != null).Select(m => m.GetInstanceID()).OrderBy(i => i);
            string key = v.archetype.GetInstanceID() + ":" + string.Join(",", ids);
            if (seen.TryGetValue(key, out var first))
                list.Add(new Problem { Asset = v, Text = $"{v.name} duplicates {first.name} (same archetype and modifiers)." });
            else seen[key] = v;
        }

        foreach (var g in modifiers.Where(m => !string.IsNullOrEmpty(m.namePrefix)).GroupBy(m => m.namePrefix).Where(g => g.Count() > 1))
            list.Add(new Problem { Asset = g.First(), Text = $"Modifiers share the name prefix '{g.Key}': {string.Join(", ", g.Select(m => m.name))}." });

        return list;
    }

    private static void RemoveEmpty(EnemyVariantSO v)
    {
        Undo.RecordObject(v, "Remove empty modifiers");
        v.modifiers.RemoveAll(m => m == null);
        EditorUtility.SetDirty(v);
        AssetDatabase.SaveAssets();
    }
}
