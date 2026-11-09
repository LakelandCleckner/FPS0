using System.Collections.Generic;
using UnityEngine;
using Combat.Enemies;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Edit-mode lineup of every enemy archetype and its variants, with each look applied live.
[ExecuteAlways]
public class EnemyGallery : MonoBehaviour
{
    [Tooltip("Gap between enemies in a row (m).")]
    [SerializeField] private float spacing = 2.5f;
    [Tooltip("Gap between archetype rows (m).")]
    [SerializeField] private float rowSpacing = 5f;
    [SerializeField] private bool showLabels = true;

#if UNITY_EDITOR
    private class Entry
    {
        public GameObject Go;
        public GameObject Source;
        public EnemyVariantSO Variant;
        public Renderer[] Renderers;
        public Vector3 BaseScale;
        public float Height;
        public float Scale = 1f;
        public int Signature;
        public bool Applied;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private MaterialPropertyBlock block;
    private double nextTick;
    private bool dirty = true;
    private GUIStyle labelStyle;

    private void OnEnable()
    {
        if (Application.isPlaying) return;
        EditorApplication.update += Tick;
        EditorApplication.projectChanged += MarkDirty;
        EditorApplication.playModeStateChanged += HandlePlayMode;
        ClearLeftovers();
        dirty = true;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        EditorApplication.projectChanged -= MarkDirty;
        EditorApplication.playModeStateChanged -= HandlePlayMode;
        Clear();
    }

    private void OnValidate() => dirty = true;
    private void MarkDirty() => dirty = true;

    private void HandlePlayMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.ExitingEditMode) Clear();
    }

    [ContextMenu("Rebuild")]
    private void RebuildNow() => dirty = true;

    private void Tick()
    {
        if (this == null || Application.isPlaying) return;
        if (EditorApplication.timeSinceStartup < nextTick && !dirty) return;
        nextTick = EditorApplication.timeSinceStartup + 0.2;

        for (int i = 0; i < entries.Count && !dirty; i++)
        {
            var e = entries[i];
            if (e.Go == null || (e.Variant != null && e.Variant.Prefab != e.Source)) dirty = true;
        }
        if (dirty) { dirty = false; Rebuild(); }

        // Look changes from inspector edits show without a rebuild.
        bool changed = false;
        foreach (var e in entries)
        {
            var look = e.Variant != null ? EnemyLook.Resolve(e.Variant.modifiers) : EnemyLook.Plain;
            int sig = look.Signature();
            if (e.Applied && sig == e.Signature) continue;
            e.Applied = true;
            e.Signature = sig;
            e.Scale = look.Scale;
            e.Go.transform.localScale = e.BaseScale * look.Scale;
            look.ApplyTo(e.Renderers, block);
            changed = true;
        }
        if (changed) SceneView.RepaintAll();
    }

    private void Rebuild()
    {
        Clear();
        if (block == null) block = new MaterialPropertyBlock();

        var archetypes = FindAll<EnemyArchetypeSO>();
        var variants = FindAll<EnemyVariantSO>();
        int row = 0;
        foreach (var arch in archetypes)
        {
            var mine = variants.FindAll(v => v.archetype == arch);
            int count = mine.Count + 1;
            float x0 = -(count - 1) * spacing * 0.5f;

            Spawn(arch.bodyPrefab, null, arch.StatSourceLabel, new Vector3(x0, 0f, row * rowSpacing));
            for (int i = 0; i < mine.Count; i++)
                Spawn(mine[i].Prefab, mine[i], mine[i].DisplayName, new Vector3(x0 + (i + 1) * spacing, 0f, row * rowSpacing));
            row++;
        }
        SceneView.RepaintAll();
    }

    private void Spawn(GameObject prefab, EnemyVariantSO variant, string label, Vector3 local)
    {
        if (prefab == null) return;
        var rot = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        var go = Instantiate(prefab, transform.TransformPoint(local), rot, transform);
        go.name = label;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;

        var e = new Entry
        {
            Go = go,
            Source = prefab,
            Variant = variant,
            Renderers = EnemyLook.FindRenderers(go),
            BaseScale = go.transform.localScale,
            Height = 2f
        };
        bool first = true;
        Bounds b = default;
        foreach (var r in e.Renderers)
        {
            if (r == null) continue;
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        if (!first) e.Height = b.max.y - go.transform.position.y;
        entries.Add(e);
    }

    private void Clear()
    {
        foreach (var e in entries)
            if (e.Go != null) DestroyImmediate(e.Go);
        entries.Clear();
    }

    // Copies left behind by a crash or an interrupted reload.
    private void ClearLeftovers()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if ((c.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(c);
        }
    }

    private static List<T> FindAll<T>() where T : Object
    {
        var list = new List<T>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) list.Add(a);
        }
        list.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        return list;
    }

    private void OnDrawGizmos()
    {
        if (!showLabels || Application.isPlaying) return;
        if (labelStyle == null)
            labelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        foreach (var e in entries)
        {
            if (e.Go == null) continue;
            float h = e.Height * Mathf.Max(0.01f, e.Scale);
            Handles.Label(e.Go.transform.position + Vector3.up * (h + 0.3f), e.Go.name, labelStyle);
        }
    }
#endif
}
