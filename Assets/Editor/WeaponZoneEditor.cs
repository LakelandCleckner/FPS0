using System;
using System.Collections.Generic;
using System.IO;
using Combat.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Paints zone id masks for FPS0/WeaponZones and FPS0/EnemyLit: one mask per material, all edited together.
public class WeaponZoneEditor : EditorWindow
{
    private const string WeaponMaskFolder = "Assets/Art/WeaponMasks";
    private const string EnemyMaskFolder = "Assets/Art/EnemyMasks";
    private const int MaxZones = ZoneSetSO.MaxZones;
    private static readonly string[] ChannelNames = { "Brightness", "Red", "Green", "Blue", "Alpha" };
    private static readonly string[] SplitProps = { "_BaseMap", "_EmissionMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap" };

    private class MeshInfo
    {
        public Vector2[] uv;
        public int[] root;
        public int[][] subTris;
        public readonly Dictionary<long, int[]> islandTris = new Dictionary<long, int[]>();   // (sub, root) -> tri starts
    }

    // One material's mask.
    [Serializable]
    private class MaskLayer
    {
        public Material material;
        public Texture2D asset;
        public byte[] zones;
        public int width;
        public int height;
        public bool dirty;
        [NonSerialized] public Texture2D preview;
        [NonSerialized] public int[] counts;
        [NonSerialized] public bool[] seen;

        public string Name => material != null ? material.name : "(missing material)";
        public bool Ready => zones != null && zones.Length == width * height && width > 0;
    }

    private class Island
    {
        public Mesh mesh;
        public int sub;
        public int root;
        public MaskLayer layer;
        public Vector3[] segments;   // outline line pairs for drawing
    }

    // One undoable change: the pixels it touched and their values before and after.
    private class ZoneEdit
    {
        public MaskLayer layer;
        public int[] idx;
        public byte[] before;
        public byte[] after;
    }

    [SerializeField] private ZoneSetSO zoneSet;
    [SerializeField] private List<MaskLayer> layers = new List<MaskLayer>();
    [SerializeField] private int activeZone = 1;
    [SerializeField] private bool pickMode;
    [SerializeField] private bool preview = true;
    [SerializeField] private ZoneShaderSO previewShader;
    [SerializeField] private int grow;
    [SerializeField] private Texture2D splitSource;
    [SerializeField] private string splitProp = "";
    [SerializeField] private int splitLayer = -1;
    [SerializeField] private int splitChannel;
    [SerializeField] private float splitMin = 0.5f;
    [SerializeField] private float splitMax = 1f;
    [SerializeField] private int splitFrom = -1;
    [SerializeField] private int splitTo = 1;
    [SerializeField] private Vector2 scroll;

    private readonly Dictionary<Mesh, MeshInfo> meshCache = new Dictionary<Mesh, MeshInfo>();
    private readonly List<(Renderer r, int index)> touched = new List<(Renderer, int)>();
    private readonly Dictionary<Renderer, Vector3[]> worldCache = new Dictionary<Renderer, Vector3[]>();
    private readonly Dictionary<(Mesh, int, int, int, int), int[]> pixelCache = new Dictionary<(Mesh, int, int, int, int), int[]>();
    private readonly List<ZoneShaderSO.ZoneLook> flatLooks = new List<ZoneShaderSO.ZoneLook>();
    private readonly List<ZoneEdit> history = new List<ZoneEdit>();
    private readonly List<MaskLayer> active = new List<MaskLayer>();
    private int appliedSteps;
    private ZoneUndoToken undoToken;
    private Mesh bakeMesh;
    private Island hover;
    private int shaderDirtyCount = -1;
    private int setDirtyCount = -1;

    [MenuItem("Tools/Zone Editor")]
    private static void Open() => GetWindow<WeaponZoneEditor>("Zones");

    private void OnEnable()
    {
        if (zoneSet == null) zoneSet = FindDefaultZoneSet();
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        Selection.selectionChanged += OnContextChanged;
        PrefabStage.prefabStageOpened += OnStageChanged;
        PrefabStage.prefabStageClosing += OnStageChanged;
        undoToken = CreateInstance<ZoneUndoToken>();
        undoToken.hideFlags = HideFlags.HideAndDontSave;
        foreach (var l in layers) RebuildLayer(l);
        SyncLayers();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        Selection.selectionChanged -= OnContextChanged;
        PrefabStage.prefabStageOpened -= OnStageChanged;
        PrefabStage.prefabStageClosing -= OnStageChanged;
        ClearPreview();
        foreach (var l in layers)
            if (l.preview != null) DestroyImmediate(l.preview);
        if (bakeMesh != null) DestroyImmediate(bakeMesh);
        if (undoToken != null)
        {
            Undo.ClearUndo(undoToken);
            DestroyImmediate(undoToken);
        }
    }

    private void OnDestroy()
    {
        if (AnyDirty() && EditorUtility.DisplayDialog("Zones", "Save the zone masks before closing?", "Save", "Discard"))
            SaveAll(false);
    }

    // Picks up edits to the preview preset or zone set without re-assigning them.
    private void OnInspectorUpdate()
    {
        int s = previewShader != null ? EditorUtility.GetDirtyCount(previewShader) : -1;
        int z = zoneSet != null ? EditorUtility.GetDirtyCount(zoneSet) : -1;
        if (s == shaderDirtyCount && z == setDirtyCount) return;
        shaderDirtyCount = s;
        setDirtyCount = z;
        ApplyPreview();
        Repaint();
    }

    private void OnUndoRedo()
    {
        if (undoToken != null && undoToken.step != appliedSteps) StepHistory(undoToken.step);
        Repaint();
    }

    private void OnStageChanged(PrefabStage stage)
    {
        ClearPreview();
        worldCache.Clear();
        hover = null;
        EditorApplication.delayCall += () => { if (this != null) { SyncLayers(); Repaint(); } };
    }

    private void OnContextChanged()
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        worldCache.Clear();
        hover = null;
        SyncLayers();
        Repaint();
    }

    private static ZoneSetSO FindDefaultZoneSet()
    {
        var guids = AssetDatabase.FindAssets("t:ZoneSetSO");
        return guids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<ZoneSetSO>(AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }

    // ------------------------------------------------------------------ layers

    // Every zone material on the open model gets a layer: its assigned mask, or a blank one at base map size.
    private void SyncLayers()
    {
        active.Clear();
        foreach (var m in ZoneMaterials())
        {
            var l = LayerFor(m);
            if (l == null)
            {
                l = new MaskLayer { material = m };
                layers.Add(l);
                var existing = m.HasProperty("_ZoneMask") ? m.GetTexture("_ZoneMask") as Texture2D : null;
                if (existing == null || !Load(l, existing)) Blank(l);
            }
            active.Add(l);
        }

        // Saved masks for models no longer open are dropped; unsaved ones stay until saved or discarded.
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            var l = layers[i];
            if (active.Contains(l) || l.dirty) continue;
            if (l.preview != null) DestroyImmediate(l.preview);
            ForgetHistory(l);
            layers.RemoveAt(i);
        }
        ApplyPreview();
    }

    private MaskLayer LayerFor(Material m)
    {
        foreach (var l in layers)
            if (l.material == m) return l;
        return null;
    }

    private bool AnyDirty()
    {
        foreach (var l in layers)
            if (l.dirty) return true;
        return false;
    }

    private bool ConfirmDiscard(IEnumerable<MaskLayer> set)
    {
        foreach (var l in set)
            if (l.dirty)
                return EditorUtility.DisplayDialog("Zones", "Discard unsaved mask changes?", "Discard", "Cancel");
        return true;
    }

    private bool[] Seen(MaskLayer l)
    {
        if (l.seen == null || l.seen.Length != l.width * l.height) l.seen = new bool[l.width * l.height];
        return l.seen;
    }

    // ------------------------------------------------------------------ edits

    // Records only the changed pixels; the undo system tracks a small token, never the masks.
    private void CommitEdit(MaskLayer l, List<int> idx, List<byte> before, string label)
    {
        if (idx.Count == 0) return;
        var e = new ZoneEdit { layer = l, idx = idx.ToArray(), before = before.ToArray(), after = new byte[idx.Count] };
        for (int k = 0; k < e.idx.Length; k++) e.after[k] = l.zones[e.idx[k]];

        if (history.Count > appliedSteps) history.RemoveRange(appliedSteps, history.Count - appliedSteps);
        history.Add(e);
        appliedSteps++;
        Undo.RecordObject(undoToken, label);
        undoToken.step = appliedSteps;
        l.dirty = true;
        UpdatePreview(l, e.idx, e.before, e.after);
    }

    private void StepHistory(int target)
    {
        while (appliedSteps > target && appliedSteps > 0)
        {
            var e = history[--appliedSteps];
            if (!e.layer.Ready) continue;
            for (int k = 0; k < e.idx.Length; k++) e.layer.zones[e.idx[k]] = e.before[k];
            e.layer.dirty = true;
            UpdatePreview(e.layer, e.idx, e.after, e.before);
        }
        while (appliedSteps < target && appliedSteps < history.Count)
        {
            var e = history[appliedSteps++];
            if (!e.layer.Ready) continue;
            for (int k = 0; k < e.idx.Length; k++) e.layer.zones[e.idx[k]] = e.after[k];
            e.layer.dirty = true;
            UpdatePreview(e.layer, e.idx, e.before, e.after);
        }
    }

    // Reloading or clearing a mask drops its undo steps, since the pixels they point at changed.
    private void ForgetHistory(MaskLayer l)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].layer != l) continue;
            history.RemoveAt(i);
            if (i < appliedSteps) appliedSteps--;
        }
        if (undoToken != null)
        {
            Undo.ClearUndo(undoToken);
            undoToken.step = appliedSteps;
        }
    }

    // ------------------------------------------------------------------ window

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawWindow();
        EditorGUILayout.EndScrollView();
    }

    private void DrawWindow()
    {
        EditorGUI.BeginChangeCheck();
        zoneSet = (ZoneSetSO)EditorGUILayout.ObjectField("Zone Set", zoneSet, typeof(ZoneSetSO), false);
        if (EditorGUI.EndChangeCheck()) ApplyPreview();

        if (zoneSet == null)
        {
            EditorGUILayout.HelpBox("Create one with Assets > Create > Combat > Zones > Zone Set.", MessageType.Info);
            return;
        }

        if (CandidateRenderers().Length == 0)
        {
            EditorGUILayout.HelpBox("Open the prefab (or select it in the scene) to pick parts.", MessageType.Info);
            return;
        }
        if (active.Count == 0)
        {
            EditorGUILayout.HelpBox($"No material uses {ZoneShaderSO.WeaponShaderName} or {ZoneShaderSO.EnemyShaderName}.", MessageType.Warning);
            if (GUILayout.Button("Refresh")) SyncLayers();
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Masks", EditorStyles.boldLabel);
        foreach (var l in active) DrawLayerRow(l);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reload From Materials") && ConfirmDiscard(active)) ReloadAll();
            if (GUILayout.Button(AnyDirty() ? "Save *" : "Save")) SaveAll(false);
            if (GUILayout.Button("Save + Assign")) SaveAll(true);
        }

        EditorGUILayout.Space();
        DrawZonePicker();
        bool wasPicking = pickMode;
        pickMode = GUILayout.Toggle(pickMode,
            pickMode ? "Picking (click: assign, Shift+click: erase)" : "Pick",
            "Button", GUILayout.Height(24));
        if (pickMode && !wasPicking) worldCache.Clear();
        grow = EditorGUILayout.IntSlider("Edge Grow (px)", grow, 0, 2);

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        preview = EditorGUILayout.Toggle("Preview", preview);
        previewShader = (ZoneShaderSO)EditorGUILayout.ObjectField("Preview Shader", previewShader, typeof(ZoneShaderSO), false);
        if (EditorGUI.EndChangeCheck()) ApplyPreview();
        if (previewShader == null)
            EditorGUILayout.HelpBox("No preview shader: zones glow in their editor colours.", MessageType.None);

        EditorGUILayout.Space();
        DrawSplit();

        EditorGUILayout.Space();
        DrawCounts();
    }

    private void DrawLayerRow(MaskLayer l)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(l.Name + (l.dirty ? " *" : ""), GUILayout.Width(140));
            EditorGUI.BeginChangeCheck();
            var picked = (Texture2D)EditorGUILayout.ObjectField(l.asset, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck() && picked != l.asset && ConfirmDiscard(new[] { l }))
            {
                if (picked == null || !Load(l, picked)) Blank(l);
            }
            if (GUILayout.Button("Clear", GUILayout.Width(48)) && ConfirmDiscard(new[] { l })) Blank(l);
            if (GUILayout.Button("Save As", GUILayout.Width(60))) Save(l, true, false);
        }

        var baseMap = BaseMap(l.material);
        if (l.Ready && baseMap != null && (baseMap.width != l.width || baseMap.height != l.height))
            EditorGUILayout.HelpBox($"{l.Name}: mask is {l.width}x{l.height}, base map is {baseMap.width}x{baseMap.height}.", MessageType.Warning);
        if (l.asset != null && l.material != null && l.material.HasProperty("_ZoneMask") && l.material.GetTexture("_ZoneMask") != l.asset)
            EditorGUILayout.HelpBox($"{l.Name}: this mask isn't assigned to the material yet (Save + Assign).", MessageType.None);
    }

    // Moves pixels into a zone where a texture channel falls in a range, e.g. shiny gloss = Plating.
    private void DrawSplit()
    {
        EditorGUILayout.LabelField("Texture Split", EditorStyles.boldLabel);

        var names = new List<string> { "All masks" };
        foreach (var l in active) names.Add(l.Name);
        splitLayer = Mathf.Clamp(splitLayer, -1, active.Count - 1);
        splitLayer = EditorGUILayout.Popup("Masks", splitLayer + 1, names.ToArray()) - 1;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("Source");
            foreach (var prop in SplitProps)
            {
                bool on = splitProp == prop;
                bool now = GUILayout.Toggle(on, prop.TrimStart('_').Replace("Map", ""), EditorStyles.miniButton);
                if (now && !on) splitProp = prop;
                else if (!now && on) splitProp = "";
            }
        }
        EditorGUI.BeginChangeCheck();
        var tex = (Texture2D)EditorGUILayout.ObjectField(string.IsNullOrEmpty(splitProp) ? "Texture" : "Or Texture", splitSource, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck())
        {
            splitSource = tex;
            if (tex != null) splitProp = "";
        }
        EditorGUILayout.LabelField(" ", string.IsNullOrEmpty(splitProp)
            ? "The same texture is used for every selected mask."
            : "Each mask uses its own material's " + splitProp.TrimStart('_') + ".", EditorStyles.miniLabel);

        splitChannel = EditorGUILayout.Popup("Channel", splitChannel, ChannelNames);
        EditorGUILayout.MinMaxSlider($"Range {splitMin:0.00}-{splitMax:0.00}", ref splitMin, ref splitMax, 0f, 1f);

        var labels = new List<string> { "Any", "Unassigned" };
        var ids = new List<int> { -1, 0 };
        foreach (var z in zoneSet.zones)
        {
            if (z == null || z.id == 0) continue;
            labels.Add(z.name);
            ids.Add(z.id);
        }
        splitFrom = ids[Mathf.Max(0, ids.IndexOf(splitFrom))];
        splitFrom = EditorGUILayout.IntPopup("Only In", splitFrom, labels.ToArray(), ids.ToArray());

        labels[0] = "Erase";
        labels.RemoveAt(1);
        ids[0] = 0;
        ids.RemoveAt(1);
        splitTo = ids[Mathf.Max(0, ids.IndexOf(splitTo))];
        splitTo = EditorGUILayout.IntPopup("Set To", splitTo, labels.ToArray(), ids.ToArray());

        GUI.enabled = !string.IsNullOrEmpty(splitProp) || splitSource != null;
        if (GUILayout.Button("Apply Split")) ApplySplit();
        GUI.enabled = true;
    }

    private void ApplySplit()
    {
        int total = 0;
        for (int n = 0; n < active.Count; n++)
        {
            if (splitLayer >= 0 && n != splitLayer) continue;
            var l = active[n];
            if (!l.Ready) continue;

            var src = splitSource;
            if (!string.IsNullOrEmpty(splitProp))
                src = l.material != null && l.material.HasProperty(splitProp) ? l.material.GetTexture(splitProp) as Texture2D : null;
            if (src == null)
            {
                Debug.LogWarning($"[Zones] {l.Name} has no {splitProp}; skipped.");
                continue;
            }
            total += Split(l, src);
        }
        Debug.Log($"[Zones] Split moved {total} px.");
    }

    private int Split(MaskLayer l, Texture2D source)
    {
        if (!ReadPixels(source, out var src, out int tw, out int th))
        {
            Debug.LogError($"[Zones] Could not read '{AssetDatabase.GetAssetPath(source)}'. Split sources must be PNG or JPG.");
            return 0;
        }

        var idx = new List<int>();
        var before = new List<byte>();
        for (int y = 0; y < l.height; y++)
        {
            int sy = y * th / l.height;
            for (int x = 0; x < l.width; x++)
            {
                int i = y * l.width + x;
                if (splitFrom >= 0 && l.zones[i] != splitFrom) continue;
                var c = src[sy * tw + x * tw / l.width];
                float v;
                switch (splitChannel)
                {
                    case 1: v = c.r / 255f; break;
                    case 2: v = c.g / 255f; break;
                    case 3: v = c.b / 255f; break;
                    case 4: v = c.a / 255f; break;
                    default: v = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f; break;
                }
                if (v < splitMin || v > splitMax || l.zones[i] == splitTo) continue;
                idx.Add(i);
                before.Add(l.zones[i]);
                l.zones[i] = (byte)splitTo;
            }
        }
        CommitEdit(l, idx, before, "Split Zones");
        return idx.Count;
    }

    private static bool ReadPixels(Texture2D source, out Color32[] px, out int w, out int h)
    {
        px = null;
        w = h = 0;
        string path = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        bool ok = tmp.LoadImage(File.ReadAllBytes(path));
        if (ok)
        {
            w = tmp.width;
            h = tmp.height;
            px = tmp.GetPixels32();
        }
        DestroyImmediate(tmp);
        return ok;
    }

    private void DrawZonePicker()
    {
        var labels = new List<string> { "Erase" };
        var ids = new List<int> { 0 };
        foreach (var z in zoneSet.zones)
        {
            if (z == null || z.id == 0) continue;
            labels.Add(z.name);
            ids.Add(z.id);
        }

        int index = Mathf.Max(0, ids.IndexOf(activeZone));
        index = GUILayout.SelectionGrid(index, labels.ToArray(), 3);
        activeZone = ids[index];
    }

    private void DrawCounts()
    {
        var total = new int[256];
        foreach (var l in active)
            if (l.counts != null)
                for (int i = 0; i < 256; i++) total[i] += l.counts[i];

        foreach (var z in zoneSet.zones)
            if (z != null && z.id > 0) EditorGUILayout.LabelField(z.name, total[z.id] + " px");

        int orphan = 0;
        for (int id = 1; id < total.Length; id++)
            if (total[id] > 0 && zoneSet.Find(id) == null) orphan += total[id];
        if (orphan == 0) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.HelpBox($"{orphan} px use zone ids not in this set (removed zones).", MessageType.Warning);
            if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(38))) ClearOrphans();
        }
    }

    private void ClearOrphans()
    {
        foreach (var l in active)
        {
            if (!l.Ready) continue;
            var idx = new List<int>();
            var before = new List<byte>();
            for (int i = 0; i < l.zones.Length; i++)
            {
                if (l.zones[i] == 0 || zoneSet.Find(l.zones[i]) != null) continue;
                idx.Add(i);
                before.Add(l.zones[i]);
                l.zones[i] = 0;
            }
            CommitEdit(l, idx, before, "Clear Removed Zones");
        }
    }

    private Color ZoneColor(int id)
    {
        if (id == 0) return Color.white;
        var z = zoneSet != null ? zoneSet.Find(id) : null;
        return z != null ? z.editorColor : Color.gray;
    }

    // ------------------------------------------------------------------ mask io

    private void ReloadAll()
    {
        foreach (var l in active)
        {
            var existing = l.material != null && l.material.HasProperty("_ZoneMask") ? l.material.GetTexture("_ZoneMask") as Texture2D : null;
            if (existing == null || !Load(l, existing)) Blank(l);
        }
    }

    private void Blank(MaskLayer l)
    {
        ForgetHistory(l);
        var baseMap = BaseMap(l.material);
        l.width = baseMap != null ? baseMap.width : 512;
        l.height = baseMap != null ? baseMap.height : 512;
        l.zones = new byte[l.width * l.height];
        l.asset = null;
        l.dirty = false;
        RebuildLayer(l);
        ApplyPreview();
    }

    private bool Load(MaskLayer l, Texture2D tex)
    {
        if (!ReadPixels(tex, out var px, out int w, out int h))
        {
            Debug.LogError($"[Zones] Could not read '{AssetDatabase.GetAssetPath(tex)}'.");
            return false;
        }

        ForgetHistory(l);
        l.width = w;
        l.height = h;
        bool legacy = IsLegacyMask(px);
        l.zones = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (legacy)
                l.zones[i] = c.r > 127 ? (byte)1 : c.g > 127 ? (byte)2 : c.b > 127 ? (byte)3 : c.a > 127 ? (byte)4 : (byte)0;
            else
                l.zones[i] = c.r <= MaxZones ? c.r : (byte)0;
        }

        l.asset = tex;
        l.dirty = legacy;
        if (legacy) Debug.Log($"[Zones] {l.Name}: converted an RGBA mask to zone ids 1-4. Save to keep the new format.");
        RebuildLayer(l);
        ApplyPreview();
        return true;
    }

    // Id masks are opaque with ids <= 16; old RGBA masks have transparent or full-channel pixels.
    private static bool IsLegacyMask(Color32[] px)
    {
        foreach (var c in px)
            if (c.a == 0 || c.r > MaxZones || c.g > MaxZones || c.b > MaxZones) return true;
        return false;
    }

    private void SaveAll(bool assign)
    {
        foreach (var l in active)
        {
            if (!l.Ready) continue;
            if (l.dirty || l.asset == null) Save(l, false, assign);
            else if (assign) Assign(l);
        }
        Repaint();
    }

    private void Save(MaskLayer l, bool saveAs, bool assign)
    {
        if (!l.Ready) return;

        string path = l.asset != null ? AssetDatabase.GetAssetPath(l.asset) : null;
        bool newPath = saveAs || string.IsNullOrEmpty(path) || !path.ToLowerInvariant().EndsWith(".png");
        if (newPath)
        {
            string folder = MaskFolder(l);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            string name = (BaseMap(l.material) != null ? BaseMap(l.material).name : l.Name) + "_Zones";
            path = saveAs
                ? EditorUtility.SaveFilePanelInProject("Save Zone Mask", name, "png", "Choose where to save the zone mask.", folder)
                : AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.png");
            if (string.IsNullOrEmpty(path)) return;
        }

        var tex = new Texture2D(l.width, l.height, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(BuildPixels(l));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        ConfigureImporter(path);

        l.asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        l.dirty = false;
        if (assign) Assign(l);
    }

    private static void Assign(MaskLayer l)
    {
        if (l.material == null || l.asset == null || !l.material.HasProperty("_ZoneMask")) return;
        Undo.RecordObject(l.material, "Assign Zone Mask");
        l.material.SetTexture("_ZoneMask", l.asset);
        EditorUtility.SetDirty(l.material);
    }

    // Mask data must stay exact: linear, point, uncompressed, no mips.
    private static void ConfigureImporter(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.SaveAndReimport();
    }

    private static string MaskFolder(MaskLayer l) =>
        l.material != null && l.material.shader != null && l.material.shader.name == ZoneShaderSO.EnemyShaderName
            ? EnemyMaskFolder : WeaponMaskFolder;

    // Zone id in RGB, opaque alpha.
    private static Color32[] BuildPixels(MaskLayer l)
    {
        var px = new Color32[l.zones.Length];
        for (int i = 0; i < l.zones.Length; i++)
        {
            byte id = l.zones[i];
            px[i] = new Color32(id, id, id, 255);
        }
        return px;
    }

    private static Texture BaseMap(Material m) =>
        m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;

    // ------------------------------------------------------------------ preview

    // Single-channel ids: a quarter of the upload of RGBA, and edits write straight into it.
    private void RebuildLayer(MaskLayer l)
    {
        if (l.counts == null) l.counts = new int[256];
        Array.Clear(l.counts, 0, l.counts.Length);
        if (!l.Ready)
        {
            if (l.preview != null) DestroyImmediate(l.preview);
            l.preview = null;
            return;
        }

        for (int i = 0; i < l.zones.Length; i++) l.counts[l.zones[i]]++;

        if (l.preview == null || l.preview.width != l.width || l.preview.height != l.height)
        {
            if (l.preview != null) DestroyImmediate(l.preview);
            l.preview = new Texture2D(l.width, l.height, TextureFormat.R8, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        l.preview.LoadRawTextureData(l.zones);
        l.preview.Apply(false);
    }

    // Changed pixels only: counts and texture bytes are patched, then one upload.
    private void UpdatePreview(MaskLayer l, int[] idx, byte[] from, byte[] to)
    {
        if (l.counts == null) RebuildLayer(l);
        for (int k = 0; k < idx.Length; k++)
        {
            l.counts[from[k]]--;
            l.counts[to[k]]++;
        }
        if (l.preview != null && l.preview.width == l.width && l.preview.height == l.height)
        {
            var raw = l.preview.GetRawTextureData<byte>();
            for (int k = 0; k < idx.Length; k++) raw[idx[k]] = to[k];
            l.preview.Apply(false);
        }
        SceneView.RepaintAll();
        Repaint();
    }

    // Property blocks are never serialized, so the prefab and materials stay untouched.
    private void ApplyPreview()
    {
        if (this == null) return;
        ClearPreview();
        if (!preview) { SceneView.RepaintAll(); return; }

        var blocks = new Dictionary<MaskLayer, MaterialPropertyBlock>();
        foreach (var r in CandidateRenderers())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!ZoneShaderSO.IsZoneShader(mats[i])) continue;
                var l = LayerFor(mats[i]);
                if (l == null || l.preview == null) continue;

                if (!blocks.TryGetValue(l, out var b))
                {
                    b = new MaterialPropertyBlock();
                    b.SetTexture("_ZoneMask", l.preview);
                    if (previewShader != null) previewShader.WriteTo(b);
                    else ZoneShaderSO.WriteZones(b, FlatLooks());
                    blocks[l] = b;
                }
                r.SetPropertyBlock(b, i);
                touched.Add((r, i));
            }
        }
        SceneView.RepaintAll();
    }

    // Each zone glows its editor colour so missed islands stand out.
    private List<ZoneShaderSO.ZoneLook> FlatLooks()
    {
        flatLooks.Clear();
        if (zoneSet == null) return flatLooks;
        foreach (var z in zoneSet.zones)
        {
            if (z == null || z.id == 0) continue;
            flatLooks.Add(new ZoneShaderSO.ZoneLook
            {
                zoneId = z.id,
                recolor = 0f,
                brightness = 1f,
                emission = z.editorColor * 0.75f
            });
        }
        return flatLooks;
    }

    private void ClearPreview()
    {
        foreach (var t in touched)
            if (t.r != null) t.r.SetPropertyBlock(null, t.index);
        touched.Clear();
    }

    // ------------------------------------------------------------------ scene

    private void OnSceneGUI(SceneView view)
    {
        if (!pickMode || zoneSet == null || active.Count == 0)
        {
            hover = null;
            return;
        }

        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

        switch (e.type)
        {
            case EventType.MouseMove:
            {
                var next = RaycastIsland(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                bool same = next != null && hover != null && next.mesh == hover.mesh && next.sub == hover.sub && next.root == hover.root;
                if (!same && (next != null || hover != null))
                {
                    hover = next;
                    view.Repaint();
                }
                break;
            }

            case EventType.MouseDown:
            {
                if (e.button != 0 || e.alt) break;
                var island = RaycastIsland(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                if (island != null) AssignIsland(island, e.shift ? 0 : activeZone);
                e.Use();
                break;
            }

            case EventType.Repaint:
                if (hover != null) DrawOutline(hover, ZoneColor(e.shift ? 0 : activeZone));
                break;
        }
    }

    // One batched draw for the whole island outline.
    private static void DrawOutline(Island island, Color color)
    {
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = color;
        Handles.DrawLines(island.segments);
    }

    private void AssignIsland(Island island, int zone)
    {
        var l = island.layer;
        if (l == null || !l.Ready) return;
        var px = IslandPixels(island);
        if (px.Length == 0) return;

        var idx = new List<int>(px.Length);
        var before = new List<byte>(px.Length);
        foreach (int i in px)
        {
            if (l.zones[i] == zone) continue;
            idx.Add(i);
            before.Add(l.zones[i]);
            l.zones[i] = (byte)zone;
        }
        if (zone > 0 && grow > 0) Grow(l, px, zone, idx, before);
        CommitEdit(l, idx, before, zone == 0 ? "Erase Zone" : "Assign Zone");
    }

    // Island pixels at its mask's size, cached per island.
    private int[] IslandPixels(Island island)
    {
        var l = island.layer;
        var key = (island.mesh, island.sub, island.root, l.width, l.height);
        if (pixelCache.TryGetValue(key, out var cached)) return cached;

        var info = Info(island.mesh);
        var tris = IslandTris(info, island.sub, island.root);
        var st = info.subTris[island.sub];
        var seen = Seen(l);
        var px = new List<int>();
        foreach (int t in tris)
            RasterTri(info.uv[st[t]], info.uv[st[t + 1]], info.uv[st[t + 2]], l.width, l.height, seen, px);
        foreach (int i in px) seen[i] = false;

        var arr = px.ToArray();
        pixelCache[key] = arr;
        return arr;
    }

    // Grows only into unassigned pixels so neighbouring zones are never stolen.
    private void Grow(MaskLayer l, int[] start, int zone, List<int> idx, List<byte> before)
    {
        var seen = Seen(l);
        var marked = new List<int>(start);
        foreach (int i in start) seen[i] = true;

        IEnumerable<int> frontier = start;
        for (int g = 0; g < grow; g++)
        {
            var next = new List<int>();
            foreach (int i in frontier)
            {
                int x = i % l.width, y = i / l.width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= l.width || ny < 0 || ny >= l.height) continue;
                        int n = ny * l.width + nx;
                        if (seen[n] || l.zones[n] != 0) continue;
                        seen[n] = true;
                        marked.Add(n);
                        idx.Add(n);
                        before.Add(0);
                        l.zones[n] = (byte)zone;
                        next.Add(n);
                    }
                }
            }
            frontier = next;
        }
        foreach (int i in marked) seen[i] = false;
    }

    // Conservative raster: any pixel the triangle touches counts.
    private static void RasterTri(Vector2 a, Vector2 b, Vector2 c, int w, int h, bool[] seen, List<int> px)
    {
        void Add(int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            int i = y * w + x;
            if (seen[i]) return;
            seen[i] = true;
            px.Add(i);
        }

        // Tiles beyond 0-1 (UDIM-style layouts) wrap onto the mask by the triangle's tile.
        var tile = new Vector2(Mathf.Floor((a.x + b.x + c.x) / 3f), Mathf.Floor((a.y + b.y + c.y) / 3f));
        a = new Vector2((a.x - tile.x) * w, (a.y - tile.y) * h);
        b = new Vector2((b.x - tile.x) * w, (b.y - tile.y) * h);
        c = new Vector2((c.x - tile.x) * w, (c.y - tile.y) * h);

        Add(Mathf.FloorToInt(a.x), Mathf.FloorToInt(a.y));
        Add(Mathf.FloorToInt(b.x), Mathf.FloorToInt(b.y));
        Add(Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y));

        float area = Edge(a, b, c);
        if (Mathf.Abs(area) < 1e-6f) return;
        if (area < 0f)
        {
            var t = b;
            b = c;
            c = t;
        }

        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 1);
        int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 1);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 1);
        int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 1);

        float oa = HalfExtent(b, c), ob = HalfExtent(c, a), oc = HalfExtent(a, b);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (Edge(b, c, p) + oa < 0f || Edge(c, a, p) + ob < 0f || Edge(a, b, p) + oc < 0f) continue;
                Add(x, y);
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
        (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    private static float HalfExtent(Vector2 u, Vector2 v) =>
        0.5f * (Mathf.Abs(v.x - u.x) + Mathf.Abs(v.y - u.y));

    // ------------------------------------------------------------------ picking

    // ------------------------------------------------------------------ picking

    // Nearest hit on any zone material; the island paints into that material's mask.
    private Island RaycastIsland(Ray ray)
    {
        Mesh bestMesh = null;
        Vector3[] bestWorld = null;
        Material bestMat = null;
        int bestSub = -1, bestTri = -1;
        float bestT = float.MaxValue;

        foreach (var r in CandidateRenderers())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            var mesh = SharedMesh(r);
            if (mesh == null) continue;
            var info = Info(mesh);
            if (info == null) continue;
            var world = WorldVertices(r, mesh);
            if (world == null) continue;
            var mats = r.sharedMaterials;

            for (int s = 0; s < info.subTris.Length; s++)
            {
                var mat = s < mats.Length ? mats[s] : null;
                if (!ZoneShaderSO.IsZoneShader(mat)) continue;
                var tris = info.subTris[s];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    if (!RayTri(ray, world[tris[i]], world[tris[i + 1]], world[tris[i + 2]], out float t)) continue;
                    if (t >= bestT) continue;
                    bestT = t;
                    bestMesh = mesh;
                    bestWorld = world;
                    bestMat = mat;
                    bestSub = s;
                    bestTri = i;
                }
            }
        }

        if (bestMesh == null) return null;
        var layer = LayerFor(bestMat);
        if (layer == null) return null;

        var bi = Info(bestMesh);
        var st = bi.subTris[bestSub];
        int root = bi.root[st[bestTri]];
        var tri = IslandTris(bi, bestSub, root);
        var seg = new Vector3[tri.Length * 6];
        for (int k = 0; k < tri.Length; k++)
        {
            int t = tri[k];
            Vector3 a = bestWorld[st[t]], b = bestWorld[st[t + 1]], c = bestWorld[st[t + 2]];
            seg[k * 6] = a; seg[k * 6 + 1] = b;
            seg[k * 6 + 2] = b; seg[k * 6 + 3] = c;
            seg[k * 6 + 4] = c; seg[k * 6 + 5] = a;
        }
        return new Island { mesh = bestMesh, sub = bestSub, root = root, layer = layer, segments = seg };
    }

    // Triangle starts of one island, cached per submesh and root.
    private static int[] IslandTris(MeshInfo info, int sub, int root)
    {
        long key = ((long)sub << 32) | (uint)root;
        if (info.islandTris.TryGetValue(key, out var cached)) return cached;
        var st = info.subTris[sub];
        var list = new List<int>();
        for (int i = 0; i < st.Length; i += 3)
            if (info.root[st[i]] == root) list.Add(i);
        var arr = list.ToArray();
        info.islandTris[key] = arr;
        return arr;
    }

    // Prefab mode: the open prefab. Otherwise: the selected scene objects.
    private static Renderer[] CandidateRenderers()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null) return stage.prefabContentsRoot.GetComponentsInChildren<Renderer>(true);

        var list = new List<Renderer>();
        foreach (var go in Selection.gameObjects)
            if (go.scene.IsValid()) list.AddRange(go.GetComponentsInChildren<Renderer>(true));
        return list.ToArray();
    }

    private static List<Material> ZoneMaterials()
    {
        var list = new List<Material>();
        foreach (var r in CandidateRenderers())
            foreach (var m in r.sharedMaterials)
                if (ZoneShaderSO.IsZoneShader(m) && !list.Contains(m)) list.Add(m);
        return list;
    }

    private static Mesh SharedMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = r.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    // Skinned meshes are baked in their current pose; cached until the selection or stage changes.
    private Vector3[] WorldVertices(Renderer r, Mesh mesh)
    {
        if (worldCache.TryGetValue(r, out var cached) && cached != null) return cached;
        var w = BakeWorld(r, mesh);
        worldCache[r] = w;
        return w;
    }

    private Vector3[] BakeWorld(Renderer r, Mesh mesh)
    {
        Vector3[] v;
        Matrix4x4 m;

        if (r is SkinnedMeshRenderer smr)
        {
            if (bakeMesh == null) bakeMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            smr.BakeMesh(bakeMesh, true);
            v = bakeMesh.vertices;
            m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        }
        else
        {
            v = mesh.vertices;
            m = r.transform.localToWorldMatrix;
        }

        if (v.Length != mesh.vertexCount) return null;
        for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
        return v;
    }

    // UV islands per mesh, cached since UV topology never changes with pose.
    private MeshInfo Info(Mesh mesh)
    {
        if (meshCache.TryGetValue(mesh, out var cached)) return cached;

        var uv = mesh.uv;
        if (uv == null || uv.Length == 0)
        {
            meshCache[mesh] = null;
            return null;
        }

        var info = new MeshInfo { uv = uv, subTris = new int[mesh.subMeshCount][] };
        for (int s = 0; s < mesh.subMeshCount; s++) info.subTris[s] = mesh.GetTriangles(s);

        var parent = new int[uv.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        var byUv = new Dictionary<long, int>();
        for (int v = 0; v < uv.Length; v++)
        {
            long k = UvKey(uv[v]);
            if (byUv.TryGetValue(k, out int rep)) Union(parent, v, rep);
            else byUv[k] = v;
        }

        foreach (var tris in info.subTris)
        {
            for (int i = 0; i < tris.Length; i += 3)
            {
                Union(parent, tris[i], tris[i + 1]);
                Union(parent, tris[i + 1], tris[i + 2]);
            }
        }

        info.root = new int[uv.Length];
        for (int v = 0; v < uv.Length; v++) info.root[v] = Find(parent, v);

        meshCache[mesh] = info;
        return info;
    }

    private static long UvKey(Vector2 uv) =>
        ((long)Mathf.RoundToInt(uv.x * 65536f) << 32) ^ (uint)Mathf.RoundToInt(uv.y * 65536f);

    private static int Find(int[] p, int x)
    {
        while (p[x] != x)
        {
            p[x] = p[p[x]];
            x = p[x];
        }
        return x;
    }

    private static void Union(int[] p, int a, int b)
    {
        a = Find(p, a);
        b = Find(p, b);
        if (a != b) p[a] = b;
    }

    // Double-sided ray/triangle test.
    private static bool RayTri(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(ray.direction, e2);
        float det = Vector3.Dot(e1, p);
        if (det == 0f) return false;

        float inv = 1f / det;
        Vector3 s = ray.origin - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;

        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(ray.direction, q) * inv;
        if (v < 0f || u + v > 1f) return false;

        t = Vector3.Dot(e2, q) * inv;
        return t > 0f;
    }
}
