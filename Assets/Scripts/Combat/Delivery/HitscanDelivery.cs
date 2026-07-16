using UnityEngine;
using Combat.Core;
using Combat.Sources;

namespace Combat.Delivery
{
    // Plain-class hitscan delivery. Stamps the attacker (ICombatant) + source on the
    // context; target is the hitbox's combatant (ICombatant). Base damage derives
    // live from the source container.
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

            var target = hitbox.combatant as ICombatant;
            if (target == null) return;

            var ctx = new HitContext
            {
                Target = target,
                HitPoint = hit.point,
                Source = HitSource.Direct,
                DamageSource = source,
                Attacker = source.Attacker,
                SourceFaction = source.Faction,
                DamageType = source.BaseDamageType,

                HitboxMultiplier = hitbox.damageMultiplier,
                BodyPartHit = hitbox.bodyPart,

                ApplyDamageToTarget = (dmg) => hitbox.combatant.TakeDamage(dmg, hitbox.bodyPart, source.BaseDamageType),
                ApplyStatusTickDamage = (dmg, type) => hitbox.combatant.TakeDamage(dmg, hitbox.bodyPart, type),

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