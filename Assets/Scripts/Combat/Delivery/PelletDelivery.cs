using UnityEngine;
using Combat.Core;
using Combat.Sources;
using Combat.Weapons;

namespace Combat.Delivery
{
    // Fires one shot as N pellets in a FIXED pattern, each through an inner delivery
    // (hitscan or projectile). Same pattern every shot — consistency is the point, and
    // what lets spread perks (Full Choke, Shot Package) be meaningfully better.
    //
    // Pattern size = spreadAngle x the weapon's live pellet_spread stat, so perks
    // tighten it through the ordinary stat pipeline.
    //
    // All pellets share the shot's ShotId and each carries 1/N of the base damage.
    // Hitscan pellets resolve synchronously inside a resolver shot group, so the whole
    // blast produces one hitmarker, one hit sound and one damage number per target.
    public class PelletDelivery : IDelivery
    {
        private readonly IDelivery inner;
        private readonly WeaponHitResolver resolver;
        private readonly Vector2[] pattern;
        private readonly float spreadAngle;

        public PelletDelivery(IDelivery inner, WeaponHitResolver resolver,
                              Vector2[] pattern, float spreadAngle)
        {
            this.inner = inner;
            this.resolver = resolver;
            this.pattern = pattern;
            this.spreadAngle = spreadAngle;
        }

        public void Fire(Vector3 origin, Vector3 direction, IDamageSource source, in ShotInfo shot)
        {
            if (inner == null || pattern == null || pattern.Length == 0) return;

            float spreadScale = source is WeaponDamageSource w ? w.ResolvedPelletSpread : 1f;
            float tanRadius = Mathf.Tan(Mathf.Clamp(spreadAngle * spreadScale, 0f, 89f) * Mathf.Deg2Rad);

            // Camera-space basis. The player camera never rolls, so world-up is a safe
            // reference; fall back if aiming straight up or down.
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(direction, right);

            int n = pattern.Length;
            resolver?.BeginShotGroup();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = pattern[i];
                    Vector3 d = (direction + (right * p.x + up * p.y) * tanRadius).normalized;
                    inner.Fire(origin, d, source, shot.ForPellet(i, n));
                }
            }
            finally
            {
                resolver?.EndShotGroup();
            }
        }
    }
}
