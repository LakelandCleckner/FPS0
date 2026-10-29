using System.Collections.Generic;
using System.IO;
using Combat.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Paints FPS0/WeaponZones id masks by clicking UV islands on the model.
public class WeaponZoneEditor : EditorWindow
{
    private const string MaskFolder = "Assets/Art/WeaponMasks";
    private const int MaxZones = WeaponZoneSetSO.MaxZones;

    private class MeshInfo
    {
        public Vector2[] uv;
        public int[] root;
        public int[][] subTris;
    }

    private class Island
    {
        public Vector2[] uvs;     // 3 per triangle
        public Vector3[] world;   // 3 per triangle
    }

    [SerializeField] private WeaponZoneSetSO zoneSet;
    [SerializeField] private Texture2D maskAsset;
    [SerializeField] private byte[] zones;      // zone id (0 = none) per pixel
    [SerializeField] private int width;
    [SerializeField] private int height;
    [SerializeField] private bool dirty;
    [SerializeField] private int activeZone = 1;
    [SerializeField] private bool pickMode;
    [SerializeField] private bool preview = true;
    [SerializeField] private WeaponShaderSO previewShader;
    [SerializeField] private int grow;

    private readonly Dictionary<Mesh, MeshInfo> meshCache = new Dictionary<Mesh, MeshInfo>();
    private readonly HashSet<Renderer> touched = new HashSet<Renderer>();
    private readonly int[] counts = new int[256];
    private readonly List<WeaponShaderSO.ZoneLook> flatLooks = new List<WeaponShaderSO.ZoneLook>();
    private MaterialPropertyBlock mpb;
    private Texture2D previewTex;
    private Mesh bakeMesh;
    private Island hover;
    private int shaderDirtyCount = -1;
    private int setDirtyCount = -1;

    [MenuItem("Tools/Weapon Zone Editor")]
    private static void Open() => GetWindow<WeaponZoneEditor>("Weapon Zones");

    private void OnEnable()
    {
        if (zoneSet == null) zoneSet = FindDefaultZoneSet();
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        Selection.selectionChanged += OnContextChanged;
        PrefabStage.prefabStageOpened += OnStageChanged;
        PrefabStage.prefabStageClosing += OnStageChanged;
        RebuildPreview();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        Selection.selectionChanged -= OnContextChanged;
        PrefabStage.prefabStageOpened -= OnStageChanged;
        PrefabStage.prefabStageClosing -= OnStageChanged;
        ClearPreview();
        if (previewTex != null) DestroyImmediate(previewTex);
        if (bakeMesh != null) DestroyImmediate(bakeMesh);
    }

    private void OnDestroy()
    {
        if (dirty && zones != null &&
            EditorUtility.DisplayDialog("Weapon Zones", "Save the zone mask before closing?", "Save", "Discard"))
            Save(false);
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
        RebuildPreview();
        Repaint();
    }

    private void OnStageChanged(PrefabStage stage)
    {
        ClearPreview();
        hover = null;
        EditorApplication.delayCall += ApplyPreview;
    }

    private void OnContextChanged()
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null) return;
        hover = null;
        ApplyPreview();
        Repaint();
    }

    private static WeaponZoneSetSO FindDefaultZoneSet()
    {
        var guids = AssetDatabase.FindAssets("t:WeaponZoneSetSO");
        return guids.Length > 0
            ? AssetDatabase.LoadAssetAtPath<WeaponZoneSetSO>(AssetDatabase.GUIDToAssetPath(guids[0]))
            : null;
    }

    // ------------------------------------------------------------------ window

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        zoneSet = (WeaponZoneSetSO)EditorGUILayout.ObjectField("Zone Set", zoneSet, typeof(WeaponZoneSetSO), false);
        if (EditorGUI.EndChangeCheck()) ApplyPreview();

        if (zoneSet == null)
        {
            EditorGUILayout.HelpBox("Create one with Assets > Create > Combat > Weapons > Weapon Zone Set.", MessageType.Info);
            return;
        }

        EditorGUI.BeginChangeCheck();
        var picked = (Texture2D)EditorGUILayout.ObjectField("Mask", maskAsset, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck() && picked != maskAsset && ConfirmDiscard())
        {
            if (picked != null) Load(picked);
            else
            {
                Undo.RecordObject(this, "Clear Zone Mask");
                maskAsset = null;
                zones = null;
                dirty = false;
                RebuildPreview();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("New") && ConfirmDiscard()) NewMask();
            GUI.enabled = zones != null;
            if (GUILayout.Button(dirty ? "Save *" : "Save")) Save(false);
            if (GUILayout.Button("Save As")) Save(true);
            GUI.enabled = true;
        }

        if (zones == null)
        {
            EditorGUILayout.HelpBox("Open the gun prefab, then click New or load an existing mask.", MessageType.Info);
            return;
        }

        if (CandidateRenderers().Length == 0)
            EditorGUILayout.HelpBox("Open the gun prefab (or select it in the scene) to pick parts.", MessageType.Warning);
        else if (!AnyZoneRenderer())
            EditorGUILayout.HelpBox($"No gun material uses {WeaponShaderSO.ShaderName}, so nothing previews.", MessageType.Warning);

        EditorGUILayout.LabelField("Size", $"{width}x{height}");
        var baseMap = BaseMapTexture();
        if (baseMap != null && (baseMap.width != width || baseMap.height != height))
            EditorGUILayout.HelpBox($"Mask size differs from the base map ({baseMap.width}x{baseMap.height}).", MessageType.Warning);

        EditorGUILayout.Space();
        DrawZonePicker();
        pickMode = GUILayout.Toggle(pickMode,
            pickMode ? "Picking (click: assign, Shift+click: erase)" : "Pick",
            "Button", GUILayout.Height(24));
        grow = EditorGUILayout.IntSlider("Edge Grow (px)", grow, 0, 2);

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        preview = EditorGUILayout.Toggle("Preview", preview);
        previewShader = (WeaponShaderSO)EditorGUILayout.ObjectField("Preview Shader", previewShader, typeof(WeaponShaderSO), false);
        if (EditorGUI.EndChangeCheck()) ApplyPreview();
        if (previewShader == null)
            EditorGUILayout.HelpBox("No preview shader: zones glow in their editor colours.", MessageType.None);

        EditorGUILayout.Space();
        DrawCounts();

        EditorGUILayout.Space();
        GUI.enabled = maskAsset != null && !dirty;
        if (GUILayout.Button("Assign Mask To Gun Materials")) AssignToMaterials();
        GUI.enabled = true;
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
        foreach (var z in zoneSet.zones)
            if (z != null && z.id > 0) EditorGUILayout.LabelField(z.name, counts[z.id] + " px");

        int orphan = 0;
        for (int id = 1; id < counts.Length; id++)
            if (counts[id] > 0 && zoneSet.Find(id) == null) orphan += counts[id];
        if (orphan == 0) return;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.HelpBox($"{orphan} px use zone ids not in this set (removed zones).", MessageType.Warning);
            if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(38))) ClearOrphans();
        }
    }

    private void ClearOrphans()
    {
        Undo.RecordObject(this, "Clear Removed Zones");
        for (int i = 0; i < zones.Length; i++)
            if (zones[i] != 0 && zoneSet.Find(zones[i]) == null) zones[i] = 0;
        dirty = true;
        RebuildPreview();
    }

    private bool ConfirmDiscard() =>
        !dirty || EditorUtility.DisplayDialog("Weapon Zones", "Discard unsaved mask changes?", "Discard", "Cancel");

    private Color ZoneColor(int id)
    {
        if (id == 0) return Color.white;
        var z = zoneSet != null ? zoneSet.Find(id) : null;
        return z != null ? z.editorColor : Color.gray;
    }

    // ------------------------------------------------------------------ mask io

    private void NewMask()
    {
        var baseMap = BaseMapTexture();
        Undo.RecordObject(this, "New Zone Mask");
        width = baseMap != null ? baseMap.width : 512;
        height = baseMap != null ? baseMap.height : 512;
        zones = new byte[width * height];
        maskAsset = null;
        dirty = true;
        RebuildPreview();
    }

    private void Load(Texture2D tex)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!tmp.LoadImage(File.ReadAllBytes(path)))
        {
            DestroyImmediate(tmp);
            Debug.LogError($"[WeaponZones] Could not read '{path}'.");
            return;
        }

        Undo.RecordObject(this, "Load Zone Mask");
        width = tmp.width;
        height = tmp.height;
        var px = tmp.GetPixels32();
        DestroyImmediate(tmp);

        bool legacy = IsLegacyMask(px);
        zones = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (legacy)
                zones[i] = c.r > 127 ? (byte)1 : c.g > 127 ? (byte)2 : c.b > 127 ? (byte)3 : c.a > 127 ? (byte)4 : (byte)0;
            else
                zones[i] = c.r <= MaxZones ? c.r : (byte)0;
        }

        maskAsset = tex;
        dirty = legacy;
        if (legacy) Debug.Log("[WeaponZones] Converted an RGBA mask to zone ids 1-4. Save to keep the new format.");
        RebuildPreview();
    }

    // Id masks are opaque with ids <= 16; old RGBA masks have transparent or full-channel pixels.
    private static bool IsLegacyMask(Color32[] px)
    {
        foreach (var c in px)
            if (c.a == 0 || c.r > MaxZones || c.g > MaxZones || c.b > MaxZones) return true;
        return false;
    }

    private void Save(bool saveAs)
    {
        if (zones == null) return;

        string path = maskAsset != null ? AssetDatabase.GetAssetPath(maskAsset) : null;
        if (saveAs || string.IsNullOrEmpty(path) || !path.ToLowerInvariant().EndsWith(".png"))
        {
            if (!AssetDatabase.IsValidFolder(MaskFolder))
            {
                Directory.CreateDirectory(MaskFolder);
                AssetDatabase.Refresh();
            }
            path = EditorUtility.SaveFilePanelInProject("Save Zone Mask", DefaultMaskName(), "png",
                "Choose where to save the zone mask.", MaskFolder);
            if (string.IsNullOrEmpty(path)) return;
        }

        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(BuildPixels());
        File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        ConfigureImporter(path);

        maskAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        dirty = false;
        Repaint();
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

    private string DefaultMaskName()
    {
        var baseMap = BaseMapTexture();
        return (baseMap != null ? baseMap.name : "Weapon") + "_Zones";
    }

    // Zone id in RGB, opaque alpha.
    private Color32[] BuildPixels()
    {
        var px = new Color32[zones.Length];
        for (int i = 0; i < zones.Length; i++)
        {
            byte id = zones[i];
            px[i] = new Color32(id, id, id, 255);
        }
        return px;
    }

    private void AssignToMaterials()
    {
        var done = new HashSet<Material>();
        foreach (var r in CandidateRenderers())
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null || m.shader.name != WeaponShaderSO.ShaderName || !done.Add(m)) continue;
                Undo.RecordObject(m, "Assign Zone Mask");
                m.SetTexture("_ZoneMask", maskAsset);
                EditorUtility.SetDirty(m);
            }
        }
        Debug.Log($"[WeaponZones] Mask assigned to {done.Count} material(s).");
    }

    // ------------------------------------------------------------------ preview

    private void RebuildPreview()
    {
        System.Array.Clear(counts, 0, counts.Length);

        if (zones == null || zones.Length != width * height)
        {
            if (previewTex != null) DestroyImmediate(previewTex);
            previewTex = null;
            ApplyPreview();
            return;
        }

        for (int i = 0; i < zones.Length; i++) counts[zones[i]]++;

        if (previewTex == null || previewTex.width != width || previewTex.height != height)
        {
            if (previewTex != null) DestroyImmediate(previewTex);
            previewTex = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        previewTex.SetPixels32(BuildPixels());
        previewTex.Apply(false);

        ApplyPreview();
        Repaint();
    }

    // Property blocks are never serialized, so the prefab and materials stay untouched.
    private void ApplyPreview()
    {
        if (this == null) return;
        ClearPreview();

        if (preview && previewTex != null)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.Clear();
            mpb.SetTexture("_ZoneMask", previewTex);
            if (previewShader != null) previewShader.WriteTo(mpb);
            else WeaponShaderSO.WriteZones(mpb, FlatLooks());

            foreach (var r in CandidateRenderers())
            {
                if (!WeaponShaderSO.UsesZoneShader(r)) continue;
                r.SetPropertyBlock(mpb);
                touched.Add(r);
            }
        }

        SceneView.RepaintAll();
    }

    // Each zone glows its editor colour so missed islands stand out.
    private List<WeaponShaderSO.ZoneLook> FlatLooks()
    {
        flatLooks.Clear();
        if (zoneSet == null) return flatLooks;
        foreach (var z in zoneSet.zones)
        {
            if (z == null || z.id == 0) continue;
            flatLooks.Add(new WeaponShaderSO.ZoneLook
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
        foreach (var r in touched)
            if (r != null) r.SetPropertyBlock(null);
        touched.Clear();
    }

    // ------------------------------------------------------------------ scene

    private void OnSceneGUI(SceneView view)
    {
        if (!pickMode || zones == null || zoneSet == null)
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
                hover = RaycastIsland(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                view.Repaint();
                break;

            case EventType.MouseDown:
                if (e.button != 0 || e.alt) break;
                var island = RaycastIsland(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                if (island != null) Assign(island, e.shift ? 0 : activeZone);
                e.Use();
                break;

            case EventType.Repaint:
                if (hover != null) DrawOutline(hover, ZoneColor(e.shift ? 0 : activeZone));
                break;
        }
    }

    private static void DrawOutline(Island island, Color color)
    {
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = color;
        var line = new Vector3[4];
        for (int i = 0; i < island.world.Length; i += 3)
        {
            line[0] = island.world[i];
            line[1] = island.world[i + 1];
            line[2] = island.world[i + 2];
            line[3] = island.world[i];
            Handles.DrawPolyLine(line);
        }
    }

    private void Assign(Island island, int zone)
    {
        var px = new List<int>();
        var seen = new bool[zones.Length];
        for (int i = 0; i < island.uvs.Length; i += 3)
            RasterTri(island.uvs[i], island.uvs[i + 1], island.uvs[i + 2], width, height, seen, px);
        if (px.Count == 0) return;

        Undo.RecordObject(this, zone == 0 ? "Erase Zone" : "Assign Zone");
        foreach (int i in px) zones[i] = (byte)zone;
        if (zone > 0) Grow(px, seen, zone);
        dirty = true;
        RebuildPreview();
    }

    // Grows only into unassigned pixels so neighbouring zones are never stolen.
    private void Grow(List<int> frontier, bool[] seen, int zone)
    {
        for (int g = 0; g < grow; g++)
        {
            var next = new List<int>();
            foreach (int i in frontier)
            {
                int x = i % width, y = i / width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        int n = ny * width + nx;
                        if (seen[n] || zones[n] != 0) continue;
                        seen[n] = true;
                        zones[n] = (byte)zone;
                        next.Add(n);
                    }
                }
            }
            frontier = next;
        }
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

        a = new Vector2(a.x * w, a.y * h);
        b = new Vector2(b.x * w, b.y * h);
        c = new Vector2(c.x * w, c.y * h);

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

    private Island RaycastIsland(Ray ray)
    {
        Mesh bestMesh = null;
        Vector3[] bestWorld = null;
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

            for (int s = 0; s < info.subTris.Length; s++)
            {
                var tris = info.subTris[s];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    if (!RayTri(ray, world[tris[i]], world[tris[i + 1]], world[tris[i + 2]], out float t)) continue;
                    if (t >= bestT) continue;
                    bestT = t;
                    bestMesh = mesh;
                    bestWorld = world;
                    bestSub = s;
                    bestTri = i;
                }
            }
        }

        if (bestMesh == null) return null;

        var bi = Info(bestMesh);
        var st = bi.subTris[bestSub];
        int root = bi.root[st[bestTri]];
        var uvs = new List<Vector2>();
        var pos = new List<Vector3>();
        for (int i = 0; i < st.Length; i += 3)
        {
            if (bi.root[st[i]] != root) continue;
            for (int k = 0; k < 3; k++)
            {
                uvs.Add(bi.uv[st[i + k]]);
                pos.Add(bestWorld[st[i + k]]);
            }
        }
        return new Island { uvs = uvs.ToArray(), world = pos.ToArray() };
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

    private static bool AnyZoneRenderer()
    {
        foreach (var r in CandidateRenderers())
            if (WeaponShaderSO.UsesZoneShader(r)) return true;
        return false;
    }

    private static Texture BaseMapTexture()
    {
        Texture fallback = null;
        foreach (var r in CandidateRenderers())
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || !m.HasProperty("_BaseMap")) continue;
                var t = m.GetTexture("_BaseMap");
                if (t == null) continue;
                if (m.shader != null && m.shader.name == WeaponShaderSO.ShaderName) return t;
                if (fallback == null) fallback = t;
            }
        }
        return fallback;
    }

    private static Mesh SharedMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = r.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    // Skinned meshes are baked in their current pose.
    private Vector3[] WorldVertices(Renderer r, Mesh mesh)
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