using UnityEngine;
using Combat.Core;
using Combat.Sources;

namespace Combat.Delivery
{
    // Reads everything from the passed-in IDamageSource. No inline stats.
    public class HitscanStrategy : MonoBehaviour, IFireStrategy
    {
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private float range = 100f;

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
