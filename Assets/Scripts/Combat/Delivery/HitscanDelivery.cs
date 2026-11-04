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

        // Reused per delivery; the nearest hit that isn't the shooter wins
        private readonly RaycastHit[] hits = new RaycastHit[16];

        public HitscanDelivery(WeaponHitResolver resolver, float range)
        {
            this.resolver = resolver;
            this.range = range;
        }

        public void Fire(Vector3 origin, Vector3 direction, IDamageSource source, in ShotInfo shot)
        {
            if (!NearestNotOwner(origin, direction, source, out var hit))
            {
                HitLog.Log($"[HitDebug:Hitscan] miss (nothing within {range:F0}m)");
                return;
            }

            var playerHitbox = hit.collider.GetComponent<PlayerHitbox>();
            if (playerHitbox != null)
            {
                HitPlayer(playerHitbox, hit, source, shot);
                return;
            }

            var hitbox = hit.collider.GetComponentInParent<EnemyHitbox>();
            if (hitbox == null)
            {
                HitLog.Log($"[HitDebug:Hitscan] {HitLog.DescribeCollider(hit.collider)} " +
                           "-> no EnemyHitbox in parents, dropped", hit.collider);
                return;
            }

            var target = hitbox.combatant as ICombatant;
            if (target == null)
            {
                HitLog.Warn($"[HitDebug:Hitscan] {HitLog.DescribeCollider(hit.collider)} " +
                            $"-> hitbox on '{hitbox.name}' has no combatant, dropped", hitbox);
                return;
            }

            HitLog.Log($"[HitDebug:Hitscan] {HitLog.DescribeHitbox(hit.collider, hitbox, hitbox.bodyPart, hitbox.damageMultiplier)}",
                       hit.collider);

            // NOTE: this context is still allocated per shot, deliberately.
            //
            // Reusing one per HitscanDelivery would be safe today - resolution is
            // synchronous and the instance fires one shot at a time - but a
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

                // Which shot this was. One hitscan shot produces one hit, so id and
                // hit are 1:1 here - unlike a piercing projectile, where several hits
                // share an id. A perk counting shots rather than hits reads this.
                Shot = shot,

                // Cached on the hitbox rather than built as a closure here. This
                // delegate OUTLIVES the hit - EffectStackPool retains it for the life
                // of any status this shot applies - so it must not capture anything
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

        // Nearest hit along the ray, skipping the shooter's own colliders (its body hitboxes, controller)
        private bool NearestNotOwner(Vector3 origin, Vector3 direction, IDamageSource source, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, direction, hits, range,
                                            PhysicsLayers.ShotMask(Physics.DefaultRaycastLayers));
            var owner = source != null ? source.Attacker as Component : null;
            Transform ownerRoot = owner != null ? owner.transform : null;

            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (ownerRoot != null && hits[i].collider.transform.IsChildOf(ownerRoot)) continue;
                if (hits[i].distance >= bestDist) continue;
                bestDist = hits[i].distance;
                best = hits[i];
            }
            return bestDist < float.MaxValue;
        }

        // A shot that lands on a player body hitbox
        private void HitPlayer(PlayerHitbox playerHitbox, RaycastHit hit, IDamageSource source, in ShotInfo shot)
        {
            var hurtbox = playerHitbox.Hurtbox;
            if (hurtbox == null || hurtbox.Target == null)
            {
                HitLog.Warn($"[HitDebug:Hitscan] {HitLog.DescribeCollider(hit.collider)} " +
                            "-> player hitbox has no PlayerHurtbox, dropped", playerHitbox);
                return;
            }

            HitLog.Log($"[HitDebug:Hitscan] {HitLog.DescribeCollider(hit.collider)} -> player {playerHitbox.bodyPart}",
                       hit.collider);

            var ctx = new HitContext
            {
                Target = hurtbox.Target,
                HitPoint = hit.point,
                Source = HitSource.Direct,
                DamageSource = source,
                Attacker = source.Attacker,
                SourceFaction = source.Faction,
                DamageType = source.BaseDamageType,
                HitboxMultiplier = playerHitbox.damageMultiplier,
                BodyPartHit = playerHitbox.bodyPart,
                Shot = shot,
                ApplyStatusTickDamage = playerHitbox.ApplyTickDamage,
                Effects = source.GetEffects(),
                MaxChainDepth = source.MaxChainDepth,
                ChainFalloff = source.ChainFalloff,
                ChainGrowth = source.ChainGrowth,
                DedupMode = source.DedupMode,
                ShowFloatingNumber = false
            };

            var attacker = source.Attacker as Component;
            hurtbox.Receive(resolver, ctx, attacker != null ? attacker.transform : null);
        }
    }
}