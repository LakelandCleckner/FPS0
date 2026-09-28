using UnityEngine;
using Combat.Sources;
using Combat.Core;

namespace Combat.Delivery
{
    // Plain-class projectile delivery. Pooled spawn; prefab/config from ProjectileSO.
    public class ProjectileDelivery : IDelivery
    {
        private readonly WeaponHitResolver resolver;
        private readonly Projectile projectilePrefab;
        private readonly Transform muzzle;
        private readonly ProjectileConfig config;

        // Reused aim-ray buffer. Sized large: NonAlloc truncation can drop the nearest hit.
        private readonly RaycastHit[] aimHits = new RaycastHit[32];

        public ProjectileDelivery(WeaponHitResolver resolver, Projectile projectilePrefab,
                                  Transform muzzle, ProjectileConfig config)
        {
            this.resolver = resolver;
            this.projectilePrefab = projectilePrefab;
            this.muzzle = muzzle;
            this.config = config;
        }

        public void Fire(Vector3 origin, Vector3 direction, IDamageSource source, in ShotInfo shot)
        {
            Vector3 spawnPos = muzzle != null ? muzzle.position : origin;

            // Converge muzzle -> crosshair aim point (origin/direction is the camera ray).
            Vector3 fireDir = muzzle != null
                ? ConvergeOnAimPoint(origin, direction, spawnPos, source)
                : direction;

            var projectile = ProjectilePool.Instance.Get(
                projectilePrefab, spawnPos, Quaternion.LookRotation(fireDir));
            if (projectile == null) return;

            projectile.Init(
                resolver: resolver,
                attacker: source.Attacker,
                damageSource: source,
                effects: source.GetEffects(),
                sourceFaction: source.Faction,
                damageType: source.BaseDamageType,
                maxChainDepth: source.MaxChainDepth,
                chainFalloff: source.ChainFalloff,
                chainGrowth: source.ChainGrowth,
                dedupMode: source.DedupMode,
                config: config.Clone(),   // snapshot -- immune to later upgrades
                direction: fireDir,
                // Carried for the whole flight, not per hit: every target a pierce
                // passes through shares this shot's id, which is exactly what stops
                // one round inflating a shot-counting perk by its pierce count.
                shot: shot);
        }

        // Nearest non-owner hit on the camera ray. Owner skip is aim-only; flight still hits owner.
        private Vector3 ConvergeOnAimPoint(Vector3 origin, Vector3 direction,
                                           Vector3 spawnPos, IDamageSource source)
        {
            var ownerComp = source.Attacker as Component;
            Transform owner = ownerComp != null ? ownerComp.transform : null;

            Vector3 aimPoint = origin + direction * config.maxDistance;

            int count = Physics.RaycastNonAlloc(origin, direction, aimHits,
                config.maxDistance, config.collisionMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = aimHits[i];
                if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    aimPoint = h.point;
                }
            }

            // Aim point behind/level with muzzle (wall-hugging): keep camera direction.
            Vector3 toAim = aimPoint - spawnPos;
            if (toAim.sqrMagnitude < 0.0001f || Vector3.Dot(toAim, direction) <= 0f)
                return direction;

            return toAim.normalized;
        }
    }
}