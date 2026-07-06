using UnityEngine;
using Combat.Core;
using Combat.Sources;

namespace Combat.Delivery
{
    // Plain-class hitscan delivery (was HitscanStrategy MonoBehaviour). Built once
    // by HitscanDeliverySO with the resolver injected. Logic unchanged from the
    // original strategy — reads everything from the passed-in IDamageSource.
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

            var target = hitbox.enemyHealth as ITargetInfo;
            if (target == null) return;

            var ctx = new HitContext
            {
                Target = target,
                HitPoint = hit.point,
                Source = HitSource.Direct,
                SourceFaction = source.Faction,
                DamageType = source.BaseDamageType,

                HitboxMultiplier = hitbox.damageMultiplier,
                BodyPartHit = hitbox.bodyPart,

                ApplyDamageToTarget = (dmg) => hitbox.enemyHealth.TakeDamage(dmg, hitbox.bodyPart, source.BaseDamageType),
                ApplyStatusTickDamage = (dmg, type) => hitbox.enemyHealth.TakeDamage(dmg, hitbox.bodyPart, type),

                Stats = source.GetStats(),
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
