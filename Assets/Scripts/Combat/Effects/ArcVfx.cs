using System.Collections.Generic;
using UnityEngine;

namespace Combat.Effects
{
    // Jagged flickering lines between two points for chain arcs: a bright core over a wide glow. Pooled, fades out.
    public sealed class ArcVfx : MonoBehaviour
    {
        private const int Points = 12;
        private const float Life = 0.3f;
        private const float CoreWidth = 0.07f;
        private const float GlowWidth = 0.35f;
        private const float Jitter = 0.45f;
        private const float FlickerInterval = 0.04f;
        private const float Intensity = 3f;

        private sealed class Arc
        {
            public LineRenderer Core;
            public LineRenderer Glow;
            public Vector3 From, To;
            public float Born, NextFlicker;
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

        // Draws one arc. material null = an additive unlit line in the colour.
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
            var mat = material != null ? material : Fallback();
            arc.Core.sharedMaterial = mat;
            arc.Glow.sharedMaterial = mat;
            arc.From = from;
            arc.To = to;
            arc.Color = color;
            arc.Born = Time.time;
            arc.NextFlicker = 0f;
            Shape(arc);
            SetColor(arc, 1f);
            arc.Core.gameObject.SetActive(true);
            arc.Glow.gameObject.SetActive(true);
            live.Add(arc);
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var arc = live[i];
                float a = 1f - (now - arc.Born) / Life;
                if (a > 0f)
                {
                    if (now >= arc.NextFlicker) Shape(arc);
                    SetColor(arc, a);
                    continue;
                }
                arc.Core.gameObject.SetActive(false);
                arc.Glow.gameObject.SetActive(false);
                live[i] = live[live.Count - 1];
                live.RemoveAt(live.Count - 1);
                free.Push(arc);
            }
        }

        // New jagged path; the glow follows the core.
        private void Shape(Arc arc)
        {
            Vector3 dir = arc.To - arc.From;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;

            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                Vector3 p = arc.From + dir * t;
                if (i > 0 && i < Points - 1)
                {
                    float k = Mathf.Sin(t * Mathf.PI) * Jitter;
                    p += side * Random.Range(-k, k) + up * Random.Range(-k, k);
                }
                buffer[i] = p;
            }
            arc.Core.positionCount = Points;
            arc.Core.SetPositions(buffer);
            arc.Glow.positionCount = Points;
            arc.Glow.SetPositions(buffer);
            arc.NextFlicker = Time.time + FlickerInterval;
        }

        private Arc Create()
        {
            return new Arc { Glow = Line("Glow", GlowWidth), Core = Line("Core", CoreWidth) };
        }

        private LineRenderer Line(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        // Both lines in a strongly saturated element hue (core slightly lighter), HDR for bloom.
        private static void SetColor(Arc arc, float alpha)
        {
            Color.RGBToHSV(arc.Color, out float h, out float s, out _);
            var hue = Color.HSVToRGB(h, Mathf.Max(s, 0.9f), 1f);

            var core = Color.Lerp(hue, Color.white, 0.2f) * Intensity;
            core.a = alpha;
            arc.Core.startColor = core;
            arc.Core.endColor = core;

            var glow = hue * Intensity;
            glow.a = alpha * 0.6f;
            arc.Glow.startColor = glow;
            arc.Glow.endColor = glow;
        }

        // Additive unlit particle material, so the glow brightens instead of covering.
        private static Material Fallback()
        {
            if (fallback != null) return fallback;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null)
            {
                fallback = new Material(shader);
                fallback.SetFloat("_Surface", 1f);
                fallback.SetFloat("_Blend", 2f);
                fallback.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fallback.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                fallback.SetFloat("_ZWrite", 0f);
                fallback.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                fallback.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                fallback = new Material(Shader.Find("Sprites/Default"));
            }
            return fallback;
        }
    }
}
