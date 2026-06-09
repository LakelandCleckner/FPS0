using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Delivery
{
    // Raycast-per-frame kinematic projectile. Carries a SNAPSHOT of everything
    // it needs at spawn (stats, effects, faction, chain config, projectile
    // config) so it never reaches back to a source that may have changed or
    // been destroyed. Built by ProjectileStrategy.Fire via Init().
    public class Projectile : MonoBehaviour
    {
        private WeaponHitResolver resolver;
        private StatBlock stats;
        private List<IHitEffect> effects;
        private int sourceFaction;
        private DamageTypeSO damageType;
        private int maxChainDepth;
        private float chainFalloff;
        private float chainGrowth;
        private HitDedupMode dedupMode;
        private ProjectileConfig config;

        private Vector3 direction;
        private float distanceTravelled;
        private float age;
        private int pierceUsed;

        // Per-projectile already-hit set, so a piercing shot never double-hits
        // the same enemy (separate from chain dedup).
        private readonly HashSet<ITargetInfo> hitTargets = new HashSet<ITargetInfo>();

        private bool initialized;

        public void Init(
            WeaponHitResolver resolver,
            StatBlock stats,
            List<IHitEffect> effects,
            int sourceFaction,
            DamageTypeSO damageType,
            int maxChainDepth,
            float chainFalloff,
            float chainGrowth,
            HitDedupMode dedupMode,
            ProjectileConfig config,
            Vector3 direction)
        {
            this.resolver = resolver;
            this.stats = stats;
            this.effects = effects;
            this.sourceFaction = sourceFaction;
            this.damageType = damageType;
            this.maxChainDepth = maxChainDepth;
            this.chainFalloff = chainFalloff;
            this.chainGrowth = chainGrowth;
            this.dedupMode = dedupMode;
            this.config = config;
            this.direction = direction.normalized;
            initialized = true;
        }

        private void Update()
        {
            if (!initialized) return;

            float step = config.speed * Time.deltaTime;
            Vector3 start = transform.position;

            // Raycast from current position along the step distance — catches
            // anything we'd pass through this frame (no tunnelling).
            if (Physics.Raycast(start, direction, out var hit, step, config.collisionMask))
            {
                HandleHit(hit);
                if (!initialized) return; // destroyed inside HandleHit
                // piercing: move to the hit point and continue past it
                transform.position = hit.point;
            }
            else
            {
                transform.position = start + direction * step;
            }

            distanceTravelled += step;
            age += Time.deltaTime;

            if (age >= config.maxLifetime || distanceTravelled >= config.maxDistance)
                Despawn();
        }

        private void HandleHit(RaycastHit hit)
        {
            var hitbox = hit.collider.GetComponentInParent<EnemyHitbox>();

            // ---- ENVIRONMENT ----
            if (hitbox == null)
            {
                // Hit something that isn't an enemy.
                if (config.stopOnEnvironment)
                    Despawn();
                return;
            }

            var target = hitbox.enemyHealth as ITargetInfo;
            if (target == null) return;

            // Pierce dedup: never hit the same enemy twice with one projectile.
            if (hitTargets.Contains(target))
                return;
            hitTargets.Add(target);

            // Build context from the carried snapshot and resolve.
            var ctx = new HitContext
            {
                Target = target,
                HitPoint = hit.point,
                SourceFaction = sourceFaction,
                DamageType = damageType,
                HitboxMultiplier = hitbox.damageMultiplier,
                BodyPartHit = hitbox.bodyPart,

                ApplyDamageToTarget = (dmg) => hitbox.enemyHealth.TakeDamage(dmg, hitbox.bodyPart, damageType),
                ApplyStatusTickDamage = (dmg, type) => hitbox.enemyHealth.TakeDamage(dmg, hitbox.bodyPart, type),

                Stats = stats,
                Effects = effects,
                MaxChainDepth = maxChainDepth,
                ChainFalloff = chainFalloff,
                ChainGrowth = chainGrowth,
                DedupMode = dedupMode
            };
            resolver.ResolveHit(ctx);

            // ---- IMPACT MODE ----
            switch (config.impactMode)
            {
                case ProjectileImpactMode.DestroyOnHit:
                    Despawn();
                    break;

                case ProjectileImpactMode.Pierce:
                    pierceUsed++;
                    if (pierceUsed > config.maxPierceCount)
                        Despawn();
                    // else keep flying
                    break;

                case ProjectileImpactMode.Embed:
                    // TODO: placed-trap/mine behaviour. For now behaves like destroy.
                    Despawn();
                    break;
            }
        }

        private void Despawn()
        {
            initialized = false;
            Destroy(gameObject);
        }
    }
}
