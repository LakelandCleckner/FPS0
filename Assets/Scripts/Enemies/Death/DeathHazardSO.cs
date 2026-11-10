using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Hazards;
using Combat.Status;

namespace Combat.Enemies
{
    // Leaves a ground pool where the enemy died that hurts or slows whoever stands in it.
    [CreateAssetMenu(fileName = "DeathHazard", menuName = "Combat/Enemies/Death Hazard")]
    public class DeathHazardSO : EnemyDeathEffectSO
    {
        public HazardPoolSpec pool = new HazardPoolSpec();

        [Header("Damage per tick")]
        public DeathDamage damage = new DeathDamage { coefficient = 0.02f };

        [Header("Status (optional)")]
        [Tooltip("Applied on every tick to everyone inside.")]
        public StatusSO applyStatus;

        public override void Trigger(in DeathEffectContext ctx)
        {
            var effects = new List<IHitEffect>(2);
            if (damage.Any) effects.Add(new DamageHitEffect(damage.Resolve(ctx).Spec()));
            if (applyStatus != null) effects.Add(new ApplyStatusHitEffect(applyStatus));
            if (effects.Count == 0 && pool.slow <= 0f) return;

            var src = ctx;
            HazardPool.Spawn(pool, ctx.Position, pool.ColorFor(damage.type), PlayerMask, EnemyMask, ctx.Owner,
                (p, inside) => Tick(inside, src, effects));
        }

        private bool Tick(List<HazardTargets.Target> inside, DeathEffectContext src, List<IHitEffect> effects)
        {
            if (effects.Count == 0) return false;

            // Copy: a kill here can set off another death effect mid-loop.
            var targets = new List<HazardTargets.Target>(inside);
            bool hit = false;
            for (int i = 0; i < targets.Count; i++)
                if (DeathEffectTargets.Hit(src, this, targets[i], effects, damage.type) > 0f) hit = true;
            return hit;
        }
    }
}
