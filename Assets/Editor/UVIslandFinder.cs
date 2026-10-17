using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Click a mesh part in the Scene view to find its UV island and export a texture mask.
public class UVIslandFinder : EditorWindow
{
    private class Pick
    {
        public Renderer renderer;
        public int subMesh;
        public string key;
        public Vector2[] uvs;     // 3 per triangle
        public Vector3[] world;   // 3 per triangle
    }

    private readonly List<Pick> picks = new List<Pick>();
    private bool pickMode;
    private int dilate = 1;
    private Texture texture;
    private Texture2D mask;
    private RectInt bounds;
    private bool hasBounds;
    private string warning;
    private Vector2 scroll;

    [MenuItem("Tools/UV Island Finder")]
    private static void Open() => GetWindow<UVIslandFinder>("UV Island Finder");

    private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        if (mask != null) DestroyImmediate(mask);
    }

    // ------------------------------------------------------------------ window

    private void OnGUI()
    {
        if (picks.RemoveAll(p => p.renderer == null) > 0) Refresh();

        EditorGUI.BeginChangeCheck();
        pickMode = GUILayout.Toggle(pickMode,
            pickMode ? "Picking: click a part (Shift+click to add/remove)" : "Pick",
            "Button", GUILayout.Height(24));
        dilate = EditorGUILayout.IntSlider("Dilate (px)", dilate, 0, 4);
        if (EditorGUI.EndChangeCheck())
        {
            RebuildMask();
            SceneView.RepaintAll();
        }

        if (picks.Count == 0)
        {
            EditorGUILayout.HelpBox("Turn on Pick, then click a part in the Scene view.", MessageType.Info);
            return;
        }

        foreach (var p in picks)
            EditorGUILayout.LabelField(p.renderer.name, $"slot {p.subMesh}, {p.uvs.Length / 3} tris");

        if (warning != null) EditorGUILayout.HelpBox(warning, MessageType.Warning);

        EditorGUILayout.LabelField("Texture", texture != null
            ? $"{texture.name} ({texture.width}x{texture.height})"
            : "none found (mask is 512x512)");

        if (hasBounds)
            EditorGUILayout.LabelField("Krita bounds",
                $"x {bounds.xMin}-{bounds.xMax - 1}, y {bounds.yMin}-{bounds.yMax - 1} " +
                $"({bounds.width}x{bounds.height})");

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear"))
            {
                picks.Clear();
                Refresh();
                GUIUtility.ExitGUI();
            }
            GUI.enabled = mask != null;
            if (GUILayout.Button("Export Mask")) ExportMask();
            GUI.enabled = true;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        float aspect = mask != null ? (float)mask.width / mask.height : 1f;
        var rect = GUILayoutUtility.GetAspectRect(aspect);
        if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        else EditorGUI.DrawRect(rect, Color.black);
        if (mask != null)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 0f, 1f, 0.6f);
            GUI.DrawTexture(rect, mask, ScaleMode.StretchToFill);
            GUI.color = prev;
        }
        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ scene

    private void OnSceneGUI(SceneView view)
    {
        DrawIslands();
        if (!pickMode) return;

        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            TryPick(HandleUtility.GUIPointToWorldRay(e.mousePosition), e.shift);
            e.Use();
        }
    }

    private void DrawIslands()
    {
        if (picks.Count == 0 || Event.current.type != EventType.Repaint) return;

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = new Color(1f, 0f, 1f, 0.9f);
        var line = new Vector3[4];
        foreach (var p in picks)
        {
            if (p.renderer == null) continue;
            for (int i = 0; i < p.world.Length; i += 3)
            {
                line[0] = p.world[i];
                line[1] = p.world[i + 1];
                line[2] = p.world[i + 2];
                line[3] = p.world[i];
                Handles.DrawPolyLine(line);
            }
        }
    }

    // ------------------------------------------------------------------ picking

    private void TryPick(Ray ray, bool add)
    {
        Renderer bestR = null;
        int bestSub = -1, bestTri = -1;
        float bestT = float.MaxValue;
        Vector3[] bestWorld = null;
        Vector2[] bestUv = null;
        int[] bestTris = null;

        foreach (var r in CandidateRenderers())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!ReadMesh(r, out var mesh, out var world, out var uv, out bool owned)) continue;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    if (!RayTri(ray, world[tris[i]], world[tris[i + 1]], world[tris[i + 2]], out float t)) continue;
                    if (t >= bestT) continue;
                    bestT = t;
                    bestR = r;
                    bestSub = s;
                    bestTri = i;
                    bestTris = tris;
                    bestWorld = world;
                    bestUv = uv;
                }
            }

            if (owned) DestroyImmediate(mesh);
        }

        if (bestR == null) return;

        var island = BuildIsland(bestTris, bestUv, bestTri, out int root);
        string key = bestR.GetInstanceID() + ":" + bestSub + ":" + root;
        int existing = picks.FindIndex(p => p.key == key);

        if (!add) picks.Clear();
        else if (existing >= 0)
        {
            picks.RemoveAt(existing);
            Refresh();
            return;
        }

        var pick = new Pick
        {
            renderer = bestR,
            subMesh = bestSub,
            key = key,
            uvs = new Vector2[island.Count * 3],
            world = new Vector3[island.Count * 3]
        };
        for (int n = 0; n < island.Count; n++)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = bestTris[island[n] + k];
                pick.uvs[n * 3 + k] = bestUv[v];
                pick.world[n * 3 + k] = bestWorld[v];
            }
        }

        picks.Add(pick);
        Refresh();
    }

    private static IEnumerable<Renderer> CandidateRenderers()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null) return stage.prefabContentsRoot.GetComponentsInChildren<Renderer>();
        return Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
    }

    // Skinned meshes are baked in their current pose; owned = caller destroys the mesh.
    private static bool ReadMesh(Renderer r, out Mesh mesh, out Vector3[] world, out Vector2[] uv, out bool owned)
    {
        mesh = null;
        world = null;
        uv = null;
        owned = false;
        Matrix4x4 m;

        if (r is SkinnedMeshRenderer smr)
        {
            if (smr.sharedMesh == null) return false;
            mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            owned = true;
            m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        }
        else
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            mesh = mf.sharedMesh;
            m = r.transform.localToWorldMatrix;
        }

        uv = mesh.uv;
        if (uv == null || uv.Length == 0)
        {
            if (owned) DestroyImmediate(mesh);
            return false;
        }

        var v = mesh.vertices;
        world = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) world[i] = m.MultiplyPoint3x4(v[i]);
        return true;
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

    // ------------------------------------------------------------------ island

    // Triangles connected through shared UV coordinates form one island.
    private static List<int> BuildIsland(int[] tris, Vector2[] uv, int hitTri, out int root)
    {
        var parent = new int[uv.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        var byUv = new Dictionary<long, int>();
        for (int i = 0; i < tris.Length; i++)
        {
            int v = tris[i];
            long k = UvKey(uv[v]);
            if (byUv.TryGetValue(k, out int rep)) Union(parent, v, rep);
            else byUv[k] = v;
        }

        for (int i = 0; i < tris.Length; i += 3)
        {
            Union(parent, tris[i], tris[i + 1]);
            Union(parent, tris[i + 1], tris[i + 2]);
        }

        root = Find(parent, tris[hitTri]);
        var island = new List<int>();
        for (int i = 0; i < tris.Length; i += 3)
            if (Find(parent, tris[i]) == root) island.Add(i);
        return island;
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

    // ------------------------------------------------------------------ mask

    private void Refresh()
    {
        warning = null;
        texture = null;
        foreach (var p in picks)
        {
            var t = TextureOf(p);
            if (texture == null) texture = t;
            else if (t != null && t != texture)
                warning = "Picked parts use different textures. The mask uses the first one.";
        }
        RebuildMask();
        Repaint();
        SceneView.RepaintAll();
    }

    private static Texture TextureOf(Pick p)
    {
        var mats = p.renderer.sharedMaterials;
        if (p.subMesh >= mats.Length || mats[p.subMesh] == null) return null;
        var m = mats[p.subMesh];
        if (m.HasProperty("_BaseMap"))
        {
            var t = m.GetTexture("_BaseMap");
            if (t != null) return t;
        }
        return m.mainTexture;
    }

    private void RebuildMask()
    {
        if (mask != null) DestroyImmediate(mask);
        mask = null;
        hasBounds = false;
        if (picks.Count == 0) return;

        int w = texture != null ? texture.width : 512;
        int h = texture != null ? texture.height : 512;

        var fill = new bool[w * h];
        foreach (var p in picks)
            for (int i = 0; i < p.uvs.Length; i += 3)
                FillTri(fill, w, h, p.uvs[i], p.uvs[i + 1], p.uvs[i + 2]);
        for (int d = 0; d < dilate; d++) fill = Dilate(fill, w, h);

        var px = new Color32[w * h];
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!fill[y * w + x]) continue;
                px[y * w + x] = new Color32(255, 255, 255, 255);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        mask = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        mask.SetPixels32(px);
        mask.Apply();

        // Texture rows start at the bottom; Krita rows start at the top.
        if (maxX >= 0)
        {
            hasBounds = true;
            bounds = new RectInt(minX, h - 1 - maxY, maxX - minX + 1, maxY - minY + 1);
        }
    }

    private static void FillTri(bool[] fill, int w, int h, Vector2 a, Vector2 b, Vector2 c)
    {
        a = new Vector2(a.x * w, a.y * h);
        b = new Vector2(b.x * w, b.y * h);
        c = new Vector2(c.x * w, c.y * h);

        float fx0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        float fx1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float fy0 = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        float fy1 = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
        if (fx1 < 0f || fy1 < 0f || fx0 >= w || fy0 >= h) return;

        // Mark vertex pixels so slivers thinner than a pixel still show.
        MarkPixel(fill, w, h, a);
        MarkPixel(fill, w, h, b);
        MarkPixel(fill, w, h, c);

        if (Mathf.Abs(Edge(a, b, c)) < 1e-8f) return;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(fx0), 0, w - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(fx1), 0, w - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(fy0), 0, h - 1);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(fy1), 0, h - 1);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float e0 = Edge(b, c, p), e1 = Edge(c, a, p), e2 = Edge(a, b, p);
                if ((e0 >= 0f && e1 >= 0f && e2 >= 0f) || (e0 <= 0f && e1 <= 0f && e2 <= 0f))
                    fill[y * w + x] = true;
            }
        }
    }

    private static void MarkPixel(bool[] fill, int w, int h, Vector2 p)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
        if (x >= 0 && x < w && y >= 0 && y < h) fill[y * w + x] = true;
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) =>
        (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    private static bool[] Dilate(bool[] src, int w, int h)
    {
        var dst = (bool[])src.Clone();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!src[y * w + x]) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h) dst[ny * w + nx] = true;
                    }
                }
            }
        }
        return dst;
    }

    private void ExportMask()
    {
        if (mask == null || picks.Count == 0) return;

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "UVMasks");
        Directory.CreateDirectory(dir);

        string name = (texture != null ? texture.name : "UV") + "_" + picks[0].renderer.name + "_mask";
        string path = EditorUtility.SaveFilePanel("Export UV Mask", dir, name, "png");
        if (string.IsNullOrEmpty(path)) return;

        File.WriteAllBytes(path, mask.EncodeToPNG());
        if (path.Replace('\\', '/').StartsWith(Application.dataPath)) AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(path);
    }
}
