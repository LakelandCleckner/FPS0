using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Enemies;

namespace Combat.Status.EditorTools
{
    // Bakes skin points for Skin auras onto every enemy and player prefab with a body. Safe to run again: rebakes.
    public static class BodySurfaceBake
    {
        private const int PointCount = 128;
        private const int Seed = 1234;
        private static readonly string[] PrefabDirs = { "Assets/Prefabs/Enemies", "Assets/Prefabs/Player" };
        private const string Tag = "[BodySurfaceBake] ";

        [MenuItem("FPS0/Art/Bake Body Surface Points", priority = 521)]
        public static void Run()
        {
            var paths = Targets();
            if (paths.Count == 0) { Debug.LogWarning(Tag + "No prefabs with a body and a StatusReceiver or PlayerHurtbox."); return; }

            // Model meshes are read here only; Read/Write goes back off afterwards.
            var restore = new List<ModelImporter>();
            foreach (var path in paths)
            {
                var smr = Body(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                if (smr == null || smr.sharedMesh == null || smr.sharedMesh.isReadable) continue;
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(smr.sharedMesh)) is ModelImporter mi && !mi.isReadable && !restore.Contains(mi))
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                    restore.Add(mi);
                }
            }

            try
            {
                foreach (var path in paths) BakePrefab(path);
            }
            finally
            {
                foreach (var mi in restore)
                {
                    mi.isReadable = false;
                    mi.SaveAndReimport();
                }
                AssetDatabase.SaveAssets();
            }
            Debug.Log(Tag + "Done.");
        }

        private static List<string> Targets()
        {
            var set = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", PrefabDirs)) Add(set, AssetDatabase.GUIDToAssetPath(guid));
            foreach (var guid in AssetDatabase.FindAssets("t:EnemyArchetypeSO"))
            {
                var arch = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (arch != null && arch.bodyPrefab != null) Add(set, AssetDatabase.GetAssetPath(arch.bodyPrefab));
            }
            return set;
        }

        private static void Add(List<string> set, string path)
        {
            if (set.Contains(path)) return;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && Host(go) != null && Body(go) != null) set.Add(path);
        }

        private static void BakePrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var host = Host(root);
                var smr = Body(root);
                if (host == null || smr == null) return;

                var baked = Bake(smr);
                if (baked.Length == 0) { Debug.LogWarning(Tag + "No points from " + smr.name + " in " + path); return; }

                var comp = host.GetComponent<BodySurfacePoints>();
                if (comp == null) comp = host.AddComponent<BodySurfacePoints>();
                comp.Set(baked, smr);
                EditorUtility.SetDirty(comp);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log(Tag + baked.Length + " points from " + smr.name + " on " + path + " (" + host.name + ")");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject Host(GameObject root)
        {
            var r = root.GetComponentInChildren<StatusReceiver>(true);
            if (r != null) return r.gameObject;
            var h = root.GetComponentInChildren<PlayerHurtbox>(true);
            return h != null ? h.gameObject : null;
        }

        // Largest skinned mesh in the prefab.
        private static SkinnedMeshRenderer Body(GameObject root)
        {
            if (root == null) return null;
            SkinnedMeshRenderer best = null;
            int most = 0;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int n = r.sharedMesh != null ? r.sharedMesh.vertexCount : 0;
                if (n > most) { most = n; best = r; }
            }
            return best;
        }

        // Area-weighted points; each keeps its two strongest bones and their bind-pose offsets.
        private static StatusAura.BodyPoint[] Bake(SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            var binds = mesh.bindposes;
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            if (bones.Length == 0 || weights.Length != verts.Length || tris.Length == 0) return System.Array.Empty<StatusAura.BodyPoint>();

            int triCount = tris.Length / 3;
            var cumulative = new float[triCount];
            float total = 0f;
            for (int i = 0; i < triCount; i++)
            {
                var p0 = verts[tris[i * 3]];
                total += Vector3.Cross(verts[tris[i * 3 + 1]] - p0, verts[tris[i * 3 + 2]] - p0).magnitude * 0.5f;
                cumulative[i] = total;
            }

            var rng = new System.Random(Seed);
            var result = new List<StatusAura.BodyPoint>(PointCount);
            var sum = new Dictionary<int, float>(8);
            for (int k = 0; k < PointCount; k++)
            {
                int t = Pick(cumulative, (float)rng.NextDouble() * total);
                int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
                float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                float w0 = 1f - u - v;
                var p = verts[i0] * w0 + verts[i1] * u + verts[i2] * v;

                sum.Clear();
                AddWeights(sum, weights[i0], w0);
                AddWeights(sum, weights[i1], u);
                AddWeights(sum, weights[i2], v);

                int ba = -1, bb = -1;
                float wa = 0f, wb = 0f;
                foreach (var kv in sum)
                {
                    if (kv.Key < 0 || kv.Key >= bones.Length || bones[kv.Key] == null) continue;
                    if (kv.Value > wa) { bb = ba; wb = wa; ba = kv.Key; wa = kv.Value; }
                    else if (kv.Value > wb) { bb = kv.Key; wb = kv.Value; }
                }
                if (ba < 0) continue;

                var point = new StatusAura.BodyPoint { a = bones[ba], la = binds[ba].MultiplyPoint3x4(p) };
                if (bb >= 0 && wb > 0f)
                {
                    point.b = bones[bb];
                    point.lb = binds[bb].MultiplyPoint3x4(p);
                    point.w = wb / (wa + wb);
                }
                result.Add(point);
            }
            return result.ToArray();
        }

        private static void AddWeights(Dictionary<int, float> sum, BoneWeight bw, float scale)
        {
            Add(sum, bw.boneIndex0, bw.weight0 * scale);
            Add(sum, bw.boneIndex1, bw.weight1 * scale);
            Add(sum, bw.boneIndex2, bw.weight2 * scale);
            Add(sum, bw.boneIndex3, bw.weight3 * scale);
        }

        private static void Add(Dictionary<int, float> sum, int bone, float w)
        {
            if (w <= 0f) return;
            sum.TryGetValue(bone, out float cur);
            sum[bone] = cur + w;
        }

        private static int Pick(float[] cumulative, float x)
        {
            int lo = 0, hi = cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] < x) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }
}
