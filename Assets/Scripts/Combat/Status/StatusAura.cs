using System.Collections.Generic;
using UnityEngine;

namespace Combat.Status
{
    // On a status aura: scales particle emission with the stack count and can emit from body points (hit hulls or baked skin).
    public sealed class StatusAura : MonoBehaviour
    {
        // A surface point held by up to two bones; w blends from a toward b.
        [System.Serializable]
        public struct BodyPoint
        {
            public Transform a;
            public Vector3 la;
            public Transform b;
            public Vector3 lb;
            public float w;

            public bool Valid => a != null;
            public Vector3 World => b == null ? a.TransformPoint(la) : Vector3.LerpUnclamped(a.TransformPoint(la), b.TransformPoint(lb), w);
        }

        private const int PointsPerBody = 96;
        private static readonly List<(MeshCollider col, float area)> hulls = new List<(MeshCollider, float)>(16);

        private ParticleSystem[] systems;
        private float[] baseRates;
        private float[] carry;
        private StatusReceiver receiver;
        private StatusSO status;
        private BodyPoint[] points;
        private float share = 1f;
        private float fade = 1f;
        private int shown = -1;

        public StatusReceiver Receiver => receiver;
        public StatusSO Status => status;
        public BodyPoint[] Points => points;
        // Fade-out strength this frame, 1 outside the fade window.
        public float Fade => fade;

        public void Bind(StatusReceiver r, StatusSO s, BodyPoint[] body)
        {
            Capture();
            receiver = r;
            status = s;
            points = body != null && body.Length > 0 ? body : null;
            shown = -1;
            share = 1f;
            fade = 1f;

            for (int i = 0; i < systems.Length; i++)
            {
                var em = systems[i].emission;
                em.enabled = points == null;
                carry[i] = 0f;
            }

            enabled = s.auraScalesWithStacks || points != null || s.fadeOutTime > 0f;
            if (!s.auraScalesWithStacks) SetRates(1f);
            else Refresh();
        }

        private void Update()
        {
            if (status.auraScalesWithStacks) Refresh();
            if (status.fadeOutTime > 0f && receiver != null)
            {
                float f = receiver.Fade(status);
                if (f != fade) { fade = f; Apply(); }
            }
            if (points != null) EmitOnBody();
        }

        private void Refresh()
        {
            int n = receiver != null ? receiver.Stacks(status) : 0;
            if (n == shown) return;
            shown = n;
            SetRates(Mathf.Clamp01((float)n / status.AuraMaxStacks));
        }

        private void SetRates(float f)
        {
            share = f;
            Apply();
        }

        private void Apply()
        {
            if (points != null) return;
            for (int i = 0; i < systems.Length; i++)
            {
                var em = systems[i].emission;
                em.rateOverTimeMultiplier = baseRates[i] * share * fade;
            }
        }

        // Emits at random hull points, at the prefab's rate times the stack share.
        private void EmitOnBody()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < systems.Length; i++)
            {
                carry[i] += baseRates[i] * share * fade * dt;
                if (carry[i] < 1f) continue;

                var ps = systems[i];
                var speed = ps.main.startSpeed;
                while (carry[i] >= 1f)
                {
                    carry[i] -= 1f;
                    var p = points[Random.Range(0, points.Length)];
                    if (!p.Valid) continue;
                    var e = new ParticleSystem.EmitParams
                    {
                        position = p.World,
                        velocity = Random.onUnitSphere * Random.Range(speed.constantMin, speed.constantMax),
                        applyShapeToPosition = false
                    };
                    ps.Emit(e, 1);
                }
            }
        }

        // Prefab rates, read once before anything changes them.
        private void Capture()
        {
            if (systems != null) return;
            systems = GetComponentsInChildren<ParticleSystem>(true);
            baseRates = new float[systems.Length];
            carry = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++) baseRates[i] = systems[i].emission.rateOverTimeMultiplier;
        }

        // Area-weighted surface points on every readable convex hull under root. Empty if there are none.
        public static BodyPoint[] Sample(Transform root)
        {
            hulls.Clear();
            float total = 0f;
            foreach (var c in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!c.convex || c.sharedMesh == null || !c.sharedMesh.isReadable) continue;
                float a = Area(c.sharedMesh, c.transform.lossyScale);
                if (a <= 0f) continue;
                hulls.Add((c, a));
                total += a;
            }
            if (hulls.Count == 0) return System.Array.Empty<BodyPoint>();

            var result = new List<BodyPoint>(PointsPerBody);
            foreach (var (col, area) in hulls)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(PointsPerBody * area / total));
                var mesh = col.sharedMesh;
                var verts = mesh.vertices;
                var tris = mesh.triangles;
                int triCount = tris.Length / 3;
                for (int k = 0; k < n; k++)
                {
                    int t = Random.Range(0, triCount) * 3;
                    result.Add(new BodyPoint { a = col.transform, la = PointOnTriangle(verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]]) });
                }
            }
            hulls.Clear();
            return result.ToArray();
        }

        public static float Area(Mesh mesh, Vector3 scale)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            float a = 0f;
            for (int i = 0; i < t.Length; i += 3)
            {
                var p0 = Vector3.Scale(v[t[i]], scale);
                var p1 = Vector3.Scale(v[t[i + 1]], scale);
                var p2 = Vector3.Scale(v[t[i + 2]], scale);
                a += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
            }
            return a;
        }

        public static Vector3 PointOnTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            float u = Random.value, w = Random.value;
            if (u + w > 1f) { u = 1f - u; w = 1f - w; }
            return a + (b - a) * u + (c - a) * w;
        }
    }
}
