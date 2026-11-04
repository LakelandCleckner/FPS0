using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Cuts skinned meshes into one point cloud per anchor bone (by dominant bone weight) for convex hitbox hulls
public static class HitboxHullCutter
{
    public sealed class Piece
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<int> Tris = new List<int>();
    }

    // ownerToAnchor: bones that start a group -> the bone its hull is parented to.
    // A vertex goes to the first owner at or above its dominant bone, below stopAt; others are dropped.
    // Vertices are returned in the anchor bone's local space, so a child at identity under the anchor fits.
    public static Dictionary<Transform, Piece> Cut(IEnumerable<SkinnedMeshRenderer> renderers,
                                                   IDictionary<Transform, Transform> ownerToAnchor,
                                                   Transform stopAt)
    {
        var pieces = new Dictionary<Transform, Piece>();
        foreach (var smr in renderers)
            if (smr != null) CutOne(smr, ownerToAnchor, stopAt, pieces);
        return pieces;
    }

    private static void CutOne(SkinnedMeshRenderer smr, IDictionary<Transform, Transform> ownerToAnchor,
                               Transform stopAt, Dictionary<Transform, Piece> pieces)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null) return;

        var src = mesh.vertices;
        var weights = mesh.boneWeights;
        var binds = mesh.bindposes;
        var bones = smr.bones;
        if (src.Length == 0 || weights.Length != src.Length)
        {
            Debug.LogWarning($"[HitboxHullCutter] '{smr.name}' has no readable skin data; enable Read/Write on its model.");
            return;
        }

        var boneAnchor = new Transform[bones.Length];
        for (int i = 0; i < bones.Length; i++) boneAnchor[i] = AnchorFor(bones[i], ownerToAnchor, stopAt);

        var anchorIndex = new Dictionary<Transform, int>();
        foreach (var a in ownerToAnchor.Values)
            if (a != null && !anchorIndex.ContainsKey(a)) anchorIndex[a] = System.Array.IndexOf(bones, a);

        var vertAnchor = new Transform[src.Length];
        var remap = new int[src.Length];
        for (int v = 0; v < src.Length; v++)
        {
            int bi = Dominant(weights[v]);
            var anchor = bi >= 0 && bi < boneAnchor.Length ? boneAnchor[bi] : null;
            if (anchor == null) continue;

            if (!pieces.TryGetValue(anchor, out var piece)) pieces[anchor] = piece = new Piece();
            int ai = anchorIndex[anchor];
            Vector3 local = ai >= 0 && ai < binds.Length
                ? binds[ai].MultiplyPoint3x4(src[v])
                : anchor.InverseTransformPoint(smr.transform.TransformPoint(src[v]));

            vertAnchor[v] = anchor;
            remap[v] = piece.Verts.Count;
            piece.Verts.Add(local);
        }

        var tris = mesh.triangles;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            var a = vertAnchor[tris[i]];
            if (a == null || a != vertAnchor[tris[i + 1]] || a != vertAnchor[tris[i + 2]]) continue;
            var list = pieces[a].Tris;
            list.Add(remap[tris[i]]);
            list.Add(remap[tris[i + 1]]);
            list.Add(remap[tris[i + 2]]);
        }
    }

    private static Transform AnchorFor(Transform b, IDictionary<Transform, Transform> ownerToAnchor, Transform stopAt)
    {
        for (var t = b; t != null && t != stopAt; t = t.parent)
            if (ownerToAnchor.TryGetValue(t, out var anchor)) return anchor;
        return null;
    }

    private static int Dominant(BoneWeight w)
    {
        int bi = w.boneIndex0; float best = w.weight0;
        if (w.weight1 > best) { best = w.weight1; bi = w.boneIndex1; }
        if (w.weight2 > best) { best = w.weight2; bi = w.boneIndex2; }
        if (w.weight3 > best) { bi = w.boneIndex3; }
        return bi;
    }

    // Builds and saves a hull source mesh; null when the piece is too small to hull
    // Directions sampled for hull points; 96 points keeps the hull under Unity's 255-face convex limit
    private const int HullDirections = 96;

    public static Mesh SaveHullMesh(Piece piece, string folder, string name)
    {
        if (piece == null || piece.Verts.Count < 4) return null;
        name = SafeFileName(name);   // asset name must match its file name

        // Only hull points matter to a convex collider: keep the furthest vertex in each of
        // evenly spread directions, so the hull stays tight but within the face limit
        var points = SupportPoints(piece.Verts, HullDirections);
        if (points.Count < 4) return null;

        var tris = new List<int>((points.Count - 2) * 3);
        for (int i = 1; i + 1 < points.Count; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(points);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        EnsureFolder(folder);
        string path = $"{folder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Overwrite in place so existing references survive a rebuild
        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }

    // Furthest vertex along each of n directions spread evenly over the sphere (duplicates removed)
    private static List<Vector3> SupportPoints(List<Vector3> verts, int n)
    {
        var picked = new HashSet<int>();
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int d = 0; d < n; d++)
        {
            float y = 1f - (d + 0.5f) * 2f / n;
            float r = Mathf.Sqrt(1f - y * y);
            float a = golden * d;
            var dir = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);

            int best = 0;
            float bestDot = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                float dot = Vector3.Dot(verts[i], dir);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            picked.Add(best);
        }

        var result = new List<Vector3>(picked.Count);
        foreach (int i in picked) result.Add(verts[i]);
        return result;
    }

    // Bone names like "mixamorig:Hips" contain characters a file name can't
    private static string SafeFileName(string name)
    {
        var chars = name.ToCharArray();
        var bad = System.IO.Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] == ':' || chars[i] == '/' || chars[i] == '\\' || System.Array.IndexOf(bad, chars[i]) >= 0)
                chars[i] = '_';
        return new string(chars);
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    // Convex MeshCollider on a child at identity under the anchor (vertices are in anchor space)
    public static MeshCollider AddHull(GameObject go, Mesh mesh, bool trigger)
    {
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;          // trigger mesh colliders must be convex; also needed on rigidbodies
        mc.isTrigger = trigger;
        return mc;
    }
}
