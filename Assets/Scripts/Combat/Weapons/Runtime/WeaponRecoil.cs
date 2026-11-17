using System.Collections.Generic;
using UnityEngine;
using Combat.Sources;

namespace Combat.Weapons
{
    // Real aim recoil, layered on the player's look as an offset. Shots follow the camera, so they follow the offset.
    // Each shot kicks along the archetype's pattern: its authored recoil pattern, or one seeded from the archetype.
    // Stability scales the kick; recoil direction leans it and squeezes its sideways wander; variance adds a little noise.
    // Pulling against the offset eats it first, so countering never pushes the aim below the start point. After the
    // centre delay the offset returns to its rest point at the archetype's centre speed.
    [DefaultExecutionOrder(-50)]
    public class WeaponRecoil : MonoBehaviour
    {
        // Simulation state, shared with the editor preview so both behave the same.
        public struct State
        {
            public Vector2 Offset;    // x up, y right, degrees
            public Vector2 Rest;      // where recentring settles
            public Vector2 Pending;   // kick still landing
            public float LandLeft;
            public float SinceShot;
            public int Shot;
        }

        private State state;
        private WeaponArchetypeSO archetype;
        private MouseLook look;

        // Current offset in degrees: x up, y right.
        public Vector2 Offset => state.Offset;

        // The recoil on the player that owns this component (lives beside MouseLook).
        public static WeaponRecoil For(Component from)
        {
            if (from == null) return null;
            var found = from.GetComponentInParent<MouseLook>();
            if (found == null) found = from.transform.root.GetComponentInChildren<MouseLook>(true);
            if (found == null) return null;
            var r = found.GetComponent<WeaponRecoil>();
            return r != null ? r : found.gameObject.AddComponent<WeaponRecoil>();
        }

        private void Awake()
        {
            look = GetComponent<MouseLook>();
            state.SinceShot = 100f;
        }

        public void Kick(WeaponDamageSource source, float aimBlend)
        {
            var w = source != null ? source.Weapon : null;
            var a = w != null ? w.archetype : null;
            if (a == null) return;

            // A different frame starts its own pattern.
            if (a != archetype) state.Shot = 0;
            archetype = a;

            var kick = ShotKick(a, state.Shot, source.ResolvedStability, source.ResolvedRecoilDirection, aimBlend,
                                Random.Range(-1f, 1f), Random.Range(-1f, 1f));
            AddKick(ref state, a, kick, Random.insideUnitCircle);
        }

        // ---------------------------------------------------------------- pattern

        // One shot's kick in degrees (x up, y right). r1 and r2 are -1..1 noise, scaled by the archetype's variance.
        public static Vector2 ShotKick(WeaponArchetypeSO a, int shot, float stability, float direction, float aimBlend, float r1, float r2)
        {
            float k = Mathf.Lerp(a.kickAtMinStability, a.kickAtMaxStability, Mathf.Clamp01(stability / 100f));
            k *= shot == 0 ? a.firstShotMultiplier : Mathf.Max(0f, a.kickRamp.Evaluate(shot));
            k *= Mathf.Lerp(1f, a.adsRecoilMultiplier, aimBlend);

            Vector2 p = PatternPoint(a, shot);
            float intensity = (100f - direction) / 100f;
            float v = a.patternVariance;

            float up = k * p.y * (1f + r1 * 0.25f * v);
            float side = k * a.sideKickScale * (Bias(direction) + p.x * intensity + r2 * Mathf.Max(intensity, 0.15f) * v);
            return new Vector2(up, side);
        }

        // The pattern's kick for a shot in base-kick units (x right, y up): authored, or seeded from the archetype.
        public static Vector2 PatternPoint(WeaponArchetypeSO a, int shot)
        {
            var list = a.recoilPattern;
            int n = list != null ? list.Count : 0;
            if (n == 0) return new Vector2(Wobble(a, shot), 1f);
            if (shot < n) return list[shot];
            int from = Mathf.Clamp(a.patternLoopFrom, 0, n - 1);
            return list[from + (shot - n) % (n - from)];
        }

        // Sideways bias from the recoil direction stat: alternates side every 10 points, vertical near values ending in 5 and at 100.
        public static float Bias(float direction)
            => Mathf.Sin((direction + 5f) * Mathf.PI / 10f) * (100f - direction) / 100f;

        // Fixed wobble in -1..1 by shot number, seeded from the archetype.
        public static float Wobble(WeaponArchetypeSO a, int shot)
            => Mathf.PerlinNoise(Seed(a), shot * 0.37f) * 2f - 1f;

        private static float Seed(WeaponArchetypeSO a)
        {
            string key = !string.IsNullOrEmpty(a.id) ? a.id : a.name;
            uint h = 2166136261u;
            foreach (char c in key) { h ^= c; h *= 16777619u; }
            return (h % 10000u) * 0.1f;
        }

        // The seeded pattern for a number of shots, as a starting point for authoring.
        public static List<Vector2> SeededPattern(WeaponArchetypeSO a, int shots)
        {
            var list = new List<Vector2>(shots);
            for (int i = 0; i < shots; i++) list.Add(new Vector2(Wobble(a, i), 1f));
            return list;
        }

        // ---------------------------------------------------------------- simulation

        // Starts a kick landing; residual is a unit-circle sample for the archetype's residual error.
        public static void AddKick(ref State s, WeaponArchetypeSO a, Vector2 kick, Vector2 residual)
        {
            s.Pending += kick;
            s.LandLeft = a.kickTime;
            s.Rest += kick * (1f - a.recenterPercent) + residual * a.residualError;
            s.SinceShot = 0f;
            s.Shot++;
        }

        // Advances landing and recentring. Returns true once the offset has settled on its rest point.
        public static bool Step(ref State s, WeaponArchetypeSO a, float dt)
        {
            s.SinceShot += dt;
            if (s.Pending != Vector2.zero)
            {
                float t = s.LandLeft > dt ? dt / s.LandLeft : 1f;
                var step = s.Pending * t;
                s.Offset += step;
                s.Pending -= step;
                s.LandLeft -= dt;
                if (t >= 1f) s.Pending = Vector2.zero;
                return false;
            }

            if (a == null || s.SinceShot < a.centreDelay) return false;
            if (s.Offset == Vector2.zero && s.Rest == Vector2.zero) { s.Shot = 0; return false; }

            s.Offset = Vector2.Lerp(s.Offset, s.Rest, 1f - Mathf.Exp(-a.centreSpeed * dt));
            return (s.Offset - s.Rest).sqrMagnitude <= 0.0004f;
        }

        // Look delta this frame in degrees (right, up). Input against the offset is eaten before it moves the look.
        public void Absorb(ref float right, ref float up)
        {
            Eat(ref state.Offset.x, ref state.Rest.x, ref up);
            Eat(ref state.Offset.y, ref state.Rest.y, ref right);
        }

        private static void Eat(ref float off, ref float restAxis, ref float input)
        {
            if (off == 0f || input == 0f || Mathf.Sign(input) == Mathf.Sign(off)) return;
            float take = Mathf.Min(Mathf.Abs(off), Mathf.Abs(input)) * Mathf.Sign(off);
            off -= take;
            input += take;
            // Rest never sits past the offset, or recentring would undo the counter.
            if (take > 0f ? restAxis > off : restAxis < off) restAxis = off;
        }

        private void Update()
        {
            if (!Step(ref state, archetype, Time.deltaTime)) return;

            // Settled: what's left becomes part of the player's look and the pattern restarts.
            if (look != null && state.Rest != Vector2.zero) look.AddLook(state.Rest.y, state.Rest.x);
            state.Offset = state.Rest = Vector2.zero;
            state.Shot = 0;
        }

        private void OnDisable()
        {
            state = new State { SinceShot = 100f };
        }
    }
}
