using System.Collections.Generic;
using UnityEngine;
using Combat.Enemies;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Enemy lineup picks (archetype, rank, layers) shared by the edit-mode look lineup here and the Play-mode effects lineup.
[ExecuteAlways]
public class EnemyGallery : MonoBehaviour
{
    public enum RankPick { All, Minor, Major, Elite, Miniboss }

    // One enemy to show: an archetype plus layers, or a hand-made variant.
    public struct Body
    {
        public EnemySpawnDef Def;
        public string Label;
        public GameObject Prefab => Def.Prefab;
    }

    [Header("Picks")]
    [Tooltip("Empty = every archetype.")]
    public EnemyArchetypeSO archetype;
    public RankPick rank = RankPick.All;
    [Tooltip("Element, shield or other layers.")]
    public List<EnemyModifierSO> layers = new List<EnemyModifierSO>();
    [Tooltip("On: one row per layer, to compare. Off: every layer on every enemy.")]
    public bool rowPerLayer = true;
    [Tooltip("Also a row with no layers when layers are picked.")]
    public bool plainRow = false;
    [Tooltip("Also show hand-made variants of the picked archetypes.")]
    public bool handMade = true;

    [Header("Layout")]
    [Tooltip("Gap between enemies in a row (m).")]
    [SerializeField] private float spacing = 2.5f;
    [Tooltip("Gap between rows (m).")]
    [SerializeField] private float rowSpacing = 5f;
    [SerializeField] private bool showLabels = true;

#if UNITY_EDITOR
    // Picked enemies as rows: per archetype, one row per layer set, one enemy per rank.
    public List<List<Body>> Rows()
    {
        var rows = new List<List<Body>>();
        var ranks = new List<EnemyRank>();
        for (int r = 0; r < EnemyTierLayers.RankNames.Length; r++)
            if (rank == RankPick.All || (int)rank == r + 1) ranks.Add((EnemyRank)r);

        var sets = new List<List<EnemyModifierSO>>();
        var picked = layers.FindAll(m => m != null && m.kind != EnemyModifierKind.Tier);
        if (picked.Count == 0 || plainRow) sets.Add(new List<EnemyModifierSO>());
        if (rowPerLayer) foreach (var m in picked) sets.Add(new List<EnemyModifierSO> { m });
        else if (picked.Count > 0) sets.Add(picked);

        var variants = handMade ? FindAll<EnemyVariantSO>() : new List<EnemyVariantSO>();
        foreach (var arch in FindAll<EnemyArchetypeSO>())
        {
            if (archetype != null && arch != archetype) continue;
            foreach (var set in sets)
            {
                var row = new List<Body>();
                foreach (var r in ranks)
                {
                    var tier = EnemyTierLayers.For(r);
                    if (r != EnemyRank.Minor && tier == null) continue;
                    var def = new EnemySpawnDef(null, arch, EnemyTierLayers.Compose(tier, set));
                    row.Add(new Body { Def = def, Label = def.DisplayName });
                }
                if (row.Count > 0) rows.Add(row);
            }
            var mine = variants.FindAll(v => v.archetype == arch);
            if (mine.Count == 0) continue;
            var hand = new List<Body>();
            foreach (var v in mine)
            {
                var def = new EnemySpawnDef(v, null, rowPerLayer ? null : picked);
                hand.Add(new Body { Def = def, Label = def.DisplayName });
            }
            rows.Add(hand);
        }
        return rows;
    }

    private class Entry
    {
        public GameObject Go;
        public List<EnemyModifierSO> Layers;
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
    public void RebuildNow() => dirty = true;

    private void Tick()
    {
        if (this == null || Application.isPlaying) return;
        if (EditorApplication.timeSinceStartup < nextTick && !dirty) return;
        nextTick = EditorApplication.timeSinceStartup + 0.2;

        for (int i = 0; i < entries.Count && !dirty; i++)
            if (entries[i].Go == null) dirty = true;
        if (dirty) { dirty = false; Rebuild(); }

        // Look changes from inspector edits show without a rebuild.
        bool changed = false;
        foreach (var e in entries)
        {
            var look = EnemyLook.Resolve(e.Layers);
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

        var rows = Rows();
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            float x0 = -(row.Count - 1) * spacing * 0.5f;
            for (int i = 0; i < row.Count; i++)
                Spawn(row[i], new Vector3(x0 + i * spacing, 0f, r * rowSpacing));
        }
        SceneView.RepaintAll();
    }

    private void Spawn(Body body, Vector3 local)
    {
        var prefab = body.Prefab;
        if (prefab == null) return;
        var rot = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        var go = Instantiate(prefab, transform.TransformPoint(local), rot, transform);
        go.name = body.Label;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;

        var layerList = new List<EnemyModifierSO>();
        body.Def.CollectLayers(layerList);
        var e = new Entry
        {
            Go = go,
            Layers = layerList,
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
