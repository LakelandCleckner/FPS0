using UnityEngine;
using Combat.Core;
using Combat.Sources;

namespace Combat.Delivery
{
    // Plain-class hitscan delivery. Built once by HitscanDeliverySO with the
    // resolver injected. Phase 2g: stamps the attacker's player-scope stats onto
    // the context (AttackerStats) so the resolver can read crit/global.
    public class HitscanDelivery : IDelivery
    {
        private readonly WeaponHitResolver resolver;
        private readonly float range;

        public HitscanDelivery(WeaponHitResolver resolver, float range)
        {
            this.resolver = resolver;
            this.range = range;
        }

        public void Fire(Vector3 origin, Vector3 direction, IDamageSource source)
        {
            if (!Physics.Raycast(origin, direction, out var hit, range))
                return;

            var hitbox = hit.collider.GetComponentInParent<EnemyHitbox>();
            if (hitbox == null) return;

            var target = hitbox.enemyHealth as ICombatant;
            if (target == null) return;

            var ctx = new HitContext
            {
                Target = target,
                HitPoint = hit.point,
                Source = HitSource.Direct,
                SourceFaction = source.Faction,
                DamageSource = source,
                DamageType = source.BaseDamageType,

                HitboxMultiplier = hitbox.damageMultiplier,
                BodyPartHit = hitbox.bodyPart,

                ApplyDamageToTarget = (dmg) => hitbox.enemyHealth.TakeDamage(dmg, hitbox.bodyPart, source.BaseDamageType),
                ApplyStatusTickDamage = (dmg, type) => hitbox.enemyHealth.TakeDamage(dmg, hitbox.bodyPart, type),

                Stats = source.GetStats(),
                AttackerStats = source.AttackerStats,
                Effects = source.GetEffects(),

                MaxChainDepth = source.MaxChainDepth,
                ChainFalloff = source.ChainFalloff,
                ChainGrowth = source.ChainGrowth,
                DedupMode = source.DedupMode
            };

            resolver.ResolveHit(ctx);
        }
    }
}