using System.Collections.Generic;
using UnityEngine;

namespace Combat.Effects
{
    // Short jagged lines between two points for chain arcs. Pooled line renderers that fade out.
    public sealed class ArcVfx : MonoBehaviour
    {
        private const int Points = 9;
        private const float Life = 0.15f;
        private const float Width = 0.06f;
        private const float Jitter = 0.35f;

        private sealed class Arc
        {
            public LineRenderer Line;
            public float Born;
            public Color Color;
        }

        private static ArcVfx instance;
        private static Material fallback;
        private readonly List<Arc> live = new List<Arc>(16);
        private readonly Stack<Arc> free = new Stack<Arc>(16);
        private readonly Vector3[] buffer = new Vector3[Points];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            fallback = null;
        }

        // Draws one arc. material null = a plain unlit line in the colour.
        public static void Draw(Vector3 from, Vector3 to, Color color, Material material)
        {
            if (instance == null)
            {
                var go = new GameObject("[ArcVfx]");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ArcVfx>();
            }
            instance.Spawn(from, to, color, material);
        }

        private void Spawn(Vector3 from, Vector3 to, Color color, Material material)
        {
            var arc = free.Count > 0 ? free.Pop() : Create();
            var line = arc.Line;
            line.sharedMaterial = material != null ? material : Fallback();

            Vector3 dir = to - from;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;

            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                Vector3 p = from + dir * t;
                if (i > 0 && i < Points - 1)
                {
                    float k = Mathf.Sin(t * Mathf.PI) * Jitter;
                    p += side * Random.Range(-k, k) + up * Random.Range(-k, k);
                }
                buffer[i] = p;
            }
            line.positionCount = Points;
            line.SetPositions(buffer);

            arc.Born = Time.time;
            arc.Color = color;
            SetColor(line, color, 1f);
            line.gameObject.SetActive(true);
            live.Add(arc);
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var arc = live[i];
                float a = 1f - (now - arc.Born) / Life;
                if (a > 0f) { SetColor(arc.Line, arc.Color, a); continue; }
                arc.Line.gameObject.SetActive(false);
                live[i] = live[live.Count - 1];
                live.RemoveAt(live.Count - 1);
                free.Push(arc);
            }
        }

        private Arc Create()
        {
            var go = new GameObject("Arc");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = Width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return new Arc { Line = line };
        }

        private static void SetColor(LineRenderer line, Color c, float alpha)
        {
            var start = c * 2f;
            start.a = alpha;
            var end = c;
            end.a = alpha * 0.6f;
            line.startColor = start;
            line.endColor = end;
        }

        private static Material Fallback()
        {
            if (fallback != null) return fallback;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            fallback = new Material(shader);
            return fallback;
        }
    }
}
