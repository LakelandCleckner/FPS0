using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Delivery
{
    // Raycast-per-frame kinematic projectile. Carries a SNAPSHOT of everything it
    // needs at spawn (source stats, effects, faction, chain config, projectile
    // config) so it never reaches back to a source that may have changed or been
    // destroyed. Built by ProjectileDelivery.Fire via Init().
    //
    // Phase 2f: the carried snapshot is now DamageStats (source-agnostic) instead
    // of the retired StatBlock.
    public class Projectile : MonoBehaviour
    {
        private WeaponHitResolver resolver;
        private DamageStats stats;
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

        private readonly HashSet<ITargetInfo> hitTargets = new HashSet<ITargetInfo>();
        private bool initialized;

        public void Init(
            WeaponHitResolver resolver,
            DamageStats stats,
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

            if (Physics.Raycast(start, direction, out var hit, step, config.collisionMask))
            {
                HandleHit(hit);
                if (!initialized) return;
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

            if (hitbox == null)
            {
                if (config.stopOnEnvironment)
                    Despawn();
                return;
            }

            var target = hitbox.enemyHealth as ITargetInfo;
            if (target == null) return;

            if (hitTargets.Contains(target))
                return;
            hitTargets.Add(target);

            var ctx = new HitContext
            {
                Target = target,
                HitPoint = hit.point,
                Source = HitSource.Direct,
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

            switch (config.impactMode)
            {
                case ProjectileImpactMode.DestroyOnHit:
                    Despawn();
                    break;
                case ProjectileImpactMode.Pierce:
                    pierceUsed++;
                    if (pierceUsed > config.maxPierceCount)
                        Despawn();
                    break;
                case ProjectileImpactMode.Embed:
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