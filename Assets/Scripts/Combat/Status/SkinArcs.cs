using UnityEngine;
using Combat.Effects;

namespace Combat.Status
{
    // On a status aura: short arcs crawling between nearby body points, with sparks thrown off both ends.
    [RequireComponent(typeof(StatusAura))]
    public sealed class SkinArcs : MonoBehaviour
    {
        [Min(0f)] public float arcsPerSecond = 2f;
        [Tooltip("Chance each arc comes with extra arcs in the same instant.")]
        [Range(0f, 1f)] public float burstChance = 0.15f;
        [Min(1)] public int burstMax = 2;
        [Min(0.01f)] public float minLength = 0.12f;
        [Min(0.02f)] public float maxLength = 0.45f;
        [Tooltip("Multiplier on the chain arc width.")]
        [Min(0.01f)] public float width = 0.25f;
        [Min(0.02f)] public float life = 0.25f;
        [Tooltip("Seconds between new jagged shapes while an arc is alive.")]
        [Min(0.01f)] public float reshapeInterval = 0.12f;
        public ParticleSystem sparks;
        [Min(0)] public int sparksPerEnd = 2;
        public Vector2 sparkSpeed = new Vector2(3f, 6f);

        private const int Tries = 6;
        private StatusAura aura;
        private float carry;

        private void OnEnable()
        {
            aura ??= GetComponent<StatusAura>();
            carry = 0f;
        }

        private void Update()
        {
            var points = aura.Points;
            var status = aura.Status;
            if (points == null || points.Length < 2 || status == null) return;

            float fade = aura.Fade;
            if (fade <= 0f) return;
            carry += arcsPerSecond * fade * Time.deltaTime;
            if (carry < 1f) return;

            var type = status.damageType;
            Color color = type != null ? type.color : Color.white;
            Material mat = type != null ? type.arcMaterial : null;
            while (carry >= 1f)
            {
                carry -= 1f;
                int n = Random.value < burstChance ? Random.Range(2, burstMax + 1) : 1;
                for (int k = 0; k < n; k++) Spark(points, color, mat);
            }
        }

        // One arc from a random point to a nearby one; skipped if no neighbour is in range.
        private void Spark(StatusAura.BodyPoint[] points, Color color, Material mat)
        {
            var a = points[Random.Range(0, points.Length)];
            if (!a.Valid) return;
            Vector3 from = a.World;
            float lo = minLength * minLength, hi = maxLength * maxLength;
            for (int t = 0; t < Tries; t++)
            {
                var b = points[Random.Range(0, points.Length)];
                if (!b.Valid) continue;
                Vector3 to = b.World;
                float d = (to - from).sqrMagnitude;
                if (d < lo || d > hi) continue;
                ArcVfx.Draw(from, to, color, mat, width, Mathf.Sqrt(d) * 0.8f, life, reshapeInterval);
                Emit(from);
                Emit(to);
                return;
            }
        }

        private void Emit(Vector3 at)
        {
            if (sparks == null || sparksPerEnd <= 0) return;
            for (int i = 0; i < sparksPerEnd; i++)
            {
                var e = new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Random.onUnitSphere * Random.Range(sparkSpeed.x, sparkSpeed.y),
                    applyShapeToPosition = false
                };
                sparks.Emit(e, 1);
            }
        }
    }
}
