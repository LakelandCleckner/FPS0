using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Effects
{
    // Pooled particle emitters that shed an element's trail along a shot. Built in code, one setup per style.
    // An emitter follows the shot head while it flies; when the shot lands it stops emitting and returns to the
    // pool once its last particles have faded.
    public static class ElementTrail
    {
        private const int MaxParticles = 120;

        private static readonly Dictionary<TrailStyle, Stack<ParticleSystem>> free = new Dictionary<TrailStyle, Stack<ParticleSystem>>();
        private static readonly List<(ParticleSystem ps, TrailStyle style, float at)> draining = new List<(ParticleSystem, TrailStyle, float)>(32);
        private static Material material;
        private static Texture2D dot;
        private static Transform root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            free.Clear();
            draining.Clear();
            material = null;
            dot = null;
            root = null;
        }

        // A playing emitter for the style at 'at', tinted. Null for None or while quitting.
        public static ParticleSystem Get(TrailStyle style, Vector3 at, Color hue, float density, float size, Transform parent)
        {
            if (style == TrailStyle.None || VfxPool.Quitting) return null;
            if (root == null) root = parent;

            ParticleSystem ps = null;
            if (free.TryGetValue(style, out var stack))
                while (stack.Count > 0 && ps == null) ps = stack.Pop();
            if (ps == null) ps = Create(style);

            ps.transform.position = at;
            var main = ps.main;
            main.startColor = hue;
            var (lo, hi) = SizeRange(style);
            float s = Mathf.Max(0.01f, size);
            main.startSize = new ParticleSystem.MinMaxCurve(lo * s, hi * s);
            var em = ps.emission;
            em.rateOverDistance = BaseRate(style) * Mathf.Max(0f, density);

            ps.gameObject.SetActive(true);
            ps.Clear(true);
            ps.Play(true);
            return ps;
        }

        // Stops emitting; the emitter is pooled again after its particles fade.
        public static void Stop(ParticleSystem ps, TrailStyle style)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            draining.Add((ps, style, Time.time + ps.main.startLifetime.constantMax + 0.05f));
        }

        // Called once per frame by TracerVfx.
        public static void Tick()
        {
            float now = Time.time;
            for (int i = draining.Count - 1; i >= 0; i--)
            {
                var d = draining[i];
                if (now < d.at) continue;
                draining[i] = draining[draining.Count - 1];
                draining.RemoveAt(draining.Count - 1);
                if (d.ps == null) continue;
                d.ps.gameObject.SetActive(false);
                if (!free.TryGetValue(d.style, out var stack)) free[d.style] = stack = new Stack<ParticleSystem>();
                stack.Push(d.ps);
            }
        }

        private static float BaseRate(TrailStyle s) => s switch
        {
            TrailStyle.Embers => 4f,
            TrailStyle.Mist => 2f,
            TrailStyle.Sparks => 5f,
            TrailStyle.Drips => 2.5f,
            _ => 0f
        };

        // Particle start size range in metres
        private static (float, float) SizeRange(TrailStyle s) => s switch
        {
            TrailStyle.Embers => (0.03f, 0.05f),
            TrailStyle.Mist => (0.12f, 0.22f),
            TrailStyle.Sparks => (0.015f, 0.03f),
            TrailStyle.Drips => (0.03f, 0.06f),
            _ => (0.03f, 0.05f)
        };

        private static ParticleSystem Create(TrailStyle style)
        {
            var go = new GameObject("Trail_" + style);
            go.SetActive(false);   // keep it from auto-playing before it is set up
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Material();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = MaxParticles;

            var em = ps.emission;
            em.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;

            switch (style)
            {
                case TrailStyle.Embers:
                    // Hot sparks that float up and shrink
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
                    main.gravityModifier = -0.15f;
                    shape.radius = 0.03f;
                    col.color = Fade(1f, 0.1f);
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
                    var noise = ps.noise;
                    noise.enabled = true;
                    noise.strength = 0.3f;
                    noise.frequency = 2f;
                    break;

                case TrailStyle.Mist:
                    // Pale cold haze that swells and hangs
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                    shape.radius = 0.05f;
                    col.color = Fade(0.35f, 0.3f);
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
                    break;

                case TrailStyle.Sparks:
                    // Fast short streaks thrown off in every direction
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
                    shape.radius = 0.02f;
                    col.color = Fade(1f, 0f);
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.5f));
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.03f;
                    r.lengthScale = 1f;
                    break;

                case TrailStyle.Drips:
                    // Droplets that fall away under gravity
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.2f);
                    main.gravityModifier = 1f;
                    shape.radius = 0.03f;
                    col.color = Fade(0.9f, 0.4f);
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));
                    break;
            }
            return ps;
        }

        // White over life (the start colour carries the hue), alpha from peak to zero; 'hold' = fraction held at peak
        private static ParticleSystem.MinMaxGradient Fade(float peak, float hold)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(peak, 0f), new GradientAlphaKey(peak, Mathf.Clamp01(hold)), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        // Additive particle material with a soft round dot
        private static Material Material()
        {
            if (material != null) return material;
            material = new Material(ArcVfx.LineMaterial()) { name = "ElementTrail" };
            var tex = Dot();
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
            return material;
        }

        private static Texture2D Dot()
        {
            if (dot != null) return dot;
            const int n = 32;
            dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "ElementTrailDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    float a = (1f - d) * (1f - d);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            dot.SetPixels(px);
            dot.Apply(false, true);
            return dot;
        }
    }
}
