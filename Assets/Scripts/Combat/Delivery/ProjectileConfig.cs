using System;
using UnityEngine;

namespace Combat.Delivery
{
    // How a projectile behaves on impact. Embed is stubbed for now (behaves like
    // DestroyOnHit) until a placed-trap/mine system exists.
    public enum ProjectileImpactMode { DestroyOnHit, Pierce, Embed }

    // Projectile parameters owned by the source, snapshotted into each spawned
    // projectile. Upgrade-mutable (pierce count, speed, etc). A hitscan weapon
    // simply has no ProjectileConfig.
    [Serializable]
    public class ProjectileConfig
    {
        [Header("Movement")]
        public float speed = 40f;
        public float maxLifetime = 5f;     // seconds before auto-despawn
        public float maxDistance = 100f;   // world units before auto-despawn

        [Header("Impact")]
        public ProjectileImpactMode impactMode = ProjectileImpactMode.DestroyOnHit;

        [Tooltip("How many enemies a Pierce projectile passes through. 0 = none. No upper cap.")]
        public int maxPierceCount = 0;

        [Tooltip("If true, hitting environment always stops the projectile even when piercing enemies.")]
        public bool stopOnEnvironment = true;

        [Header("Collision")]
        [Tooltip("Layers the projectile's per-frame raycast checks (enemies + environment).")]
        public LayerMask collisionMask = ~0;

        // Returns a copy so the projectile's snapshot can't be mutated by later
        // upgrades to the source mid-flight.
        public ProjectileConfig Clone()
        {
            return new ProjectileConfig
            {
                speed = speed,
                maxLifetime = maxLifetime,
                maxDistance = maxDistance,
                impactMode = impactMode,
                maxPierceCount = maxPierceCount,
                stopOnEnvironment = stopOnEnvironment,
                collisionMask = collisionMask
            };
        }

        // Upgrade hook: add pierce, flipping mode on automatically. No upper cap.
        public void AddPierce(int amount)
        {
            maxPierceCount += amount;
            if (maxPierceCount > 0 && impactMode == ProjectileImpactMode.DestroyOnHit)
                impactMode = ProjectileImpactMode.Pierce;
        }
    }
}
