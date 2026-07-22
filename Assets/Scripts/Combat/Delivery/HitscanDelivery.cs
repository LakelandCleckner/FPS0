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

            // NOTE: this context is still allocated per shot, deliberately.
            //
            // Reusing one per HitscanDelivery would be safe today — resolution is
            // synchronous and the instance fires one shot at a time — but a
            // perk-contributed effect that causes the same weapon to fire again would
            // re-enter Fire and clobber the context mid-resolution. Projectiles don't
            // have this problem because each in-flight projectile owns its own.
            // Revisit with a re-entrancy guard if profiling says it matters.
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

                // Cached on the hitbox rather than built as a closure here. This
                // delegate OUTLIVES the hit — EffectStackPool retains it for the life
                // of any status this shot applies — so it must not capture anything
                // shot-scoped. It also removes an allocation from every hitscan shot.
                ApplyStatusTickDamage = hitbox.ApplyTickDamage,

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