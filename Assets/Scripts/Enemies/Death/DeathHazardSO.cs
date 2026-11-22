using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Hazards;
using Combat.Status;
using Combat.Info;

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

        public override void DescribeAs(Describer d, string lead)
        {
            d.Add(lead).Add("leaves ").Keyword("pool", "a pool");
            if (useEnemyElement) d.Add(" of its element");
            else if (damage.type != null) d.Add(" of ").Element(damage.type);
            d.Add(" (").Meters(pool.radius, "radius").Add(" radius, lasts ").Seconds(pool.duration, "duration").Add(")");
            bool dealt = damage.Any;
            if (dealt) { d.Add(" that deals "); damage.Describe(d, "damage", useEnemyElement); d.Add(" every ").Seconds(pool.tickInterval, "tick"); }
            if (pool.slow > 0f) d.Add(dealt ? " and " : " that ").Keyword("slow", "slows").Add(" everyone inside by ").Percent(pool.slow, "slow");
            if (applyStatus != null) d.Add(", applying ").StatusLink(applyStatus).Add(" each tick");
            else if (useEnemyElement) d.Add(", applying its element's status each tick");
            d.Add(".");
            DescribeTargets(d);
        }

        public override void Trigger(in DeathEffectContext ctx)
        {
            var type = ElementFor(ctx, damage.type);
            var status = StatusFor(type, applyStatus);
            var effects = new List<IHitEffect>(2);
            if (damage.Any) effects.Add(new DamageHitEffect(damage.Resolve(ctx, type).Spec()));
            if (status != null) effects.Add(new ApplyStatusHitEffect(status));
            if (effects.Count == 0 && pool.slow <= 0f) return;

            var src = ctx;
            HazardPool.Spawn(pool, ctx.Position, pool.ColorFor(type), PlayerMask, EnemyMask, ctx.Owner,
                (p, inside) => Tick(inside, src, effects, type));
        }

        private bool Tick(List<HazardTargets.Target> inside, DeathEffectContext src, List<IHitEffect> effects, DamageTypeSO type)
        {
            if (effects.Count == 0) return false;

            // Copy: a kill here can set off another death effect mid-loop.
            var targets = new List<HazardTargets.Target>(inside);
            bool hit = false;
            for (int i = 0; i < targets.Count; i++)
                if (DeathEffectTargets.Hit(src, this, targets[i], effects, type) > 0f) hit = true;
            return hit;
        }
    }
}
