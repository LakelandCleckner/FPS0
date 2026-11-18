using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Events;
using Combat.Info;
using Combat.Status;

namespace Combat.Perks
{
    public enum HitCondition
    {
        WithinDistance,     // shot origin to impact at or under the distance
        BeyondDistance,     // shot origin to impact over the distance
        TargetBelowHealth,  // target health fraction at or under the threshold, before this hit
        TargetAboveHealth,  // target health fraction at or over the threshold, before this hit
        TargetDebuffed,     // target carries a status (burn, poison, ...)
        Precision,          // the hit struck a precision zone
        TargetHasStatus     // target carries any of the listed statuses
    }

    // Extra damage on hits that meet a condition. Contributes a Modifier-phase effect to the resolving hit,
    // so it runs through the normal damage formula; crit is already rolled and is never touched.
    [CreateAssetMenu(fileName = "HitConditionPerk", menuName = "Combat/Perks/Hit Condition Perk")]
    public class HitConditionPerkSO : PerkSO
    {
        [Header("Condition")]
        public HitCondition condition = HitCondition.WithinDistance;
        [Tooltip("Within / Beyond Distance: metres from the shot's origin.")]
        [Min(0f)] public float distance = 6f;
        [Tooltip("Target Below / Above Health: fraction of max health (0.3 = 30%).")]
        [Range(0f, 1f)] public float healthFraction = 0.3f;
        [Tooltip("Target Has Status: any of these counts.")]
        public List<StatusSO> statuses = new List<StatusSO>();
        [Tooltip("Which hits count. Direct = shots; add Status Tick for burns and poison.")]
        public HitSourceMask sources = HitSourceMask.Direct;

        [Header("Bonus")]
        [Tooltip("Extra damage as a fraction (0.25 = 25% more).")]
        public float bonus = 0.25f;

        public override string TriggerGroup => "On hit";

        protected override void DescribeAuto(Describer d)
        {
            d.Add("Deals ").Percent(bonus, "bonus").Add(" more damage ");
            switch (condition)
            {
                case HitCondition.WithinDistance: d.Add("to targets within ").Meters(distance, "distance"); break;
                case HitCondition.BeyondDistance: d.Add("to targets beyond ").Meters(distance, "distance"); break;
                case HitCondition.TargetBelowHealth: d.Add("to targets at or below ").Percent(healthFraction, "health").Add(" health"); break;
                case HitCondition.TargetAboveHealth: d.Add("to targets at or above ").Percent(healthFraction, "health").Add(" health"); break;
                case HitCondition.TargetDebuffed: d.Add("to targets suffering a status effect"); break;
                case HitCondition.Precision: d.Add("on ").Keyword("precision", "precision").Add(" hits"); break;
                case HitCondition.TargetHasStatus:
                    d.Add("to targets with ");
                    for (int i = 0; i < statuses.Count; i++)
                    {
                        if (i > 0) d.Add(i == statuses.Count - 1 ? " or " : ", ");
                        d.StatusLink(statuses[i]);
                    }
                    break;
            }
            d.Add(".");
            if ((sources & HitSourceMask.StatusTick) != 0) d.Next().Add("Status ticks count.");
        }

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime, IHitEffectContributor
        {
            private Bonus effect;

            private HitConditionPerkSO Def => (HitConditionPerkSO)Definition;

            protected override void OnAttach()
            {
                effect = new Bonus(this);
                if (Ctx.Bus != null) Ctx.Bus.RegisterContributor(this);
            }

            protected override void OnDetach()
            {
                if (Ctx.Bus != null) Ctx.Bus.UnregisterContributor(this);
            }

            public void Contribute(HitContext ctx, List<IHitEffect> into)
            {
                if (!Hears || ctx.Target == null || !Def.sources.Allows(ctx.Source)) return;
                // Weapon perk: its own weapon's hits. Armor perk: any hit by the player.
                if (Ctx.IsArmor ? !ReferenceEquals(ctx.Attacker, Ctx.Player) : !ReferenceEquals(ctx.DamageSource, Ctx.Weapon)) return;
                if (Met(ctx)) into.Add(effect);
            }

            private bool Met(HitContext ctx)
            {
                var d = Def;
                switch (d.condition)
                {
                    case HitCondition.WithinDistance:
                        return ctx.Shot.HasOrigin && Vector3.Distance(ctx.HitPoint, ctx.Shot.Origin) <= d.distance;
                    case HitCondition.BeyondDistance:
                        return ctx.Shot.HasOrigin && Vector3.Distance(ctx.HitPoint, ctx.Shot.Origin) > d.distance;
                    case HitCondition.TargetBelowHealth:
                        return ctx.Target.MaxHealth > 0f && ctx.Target.CurrentHealth / ctx.Target.MaxHealth <= d.healthFraction;
                    case HitCondition.TargetAboveHealth:
                        return ctx.Target.MaxHealth > 0f && ctx.Target.CurrentHealth / ctx.Target.MaxHealth >= d.healthFraction;
                    case HitCondition.TargetDebuffed:
                        return ctx.Target.IsDebuffed;
                    case HitCondition.Precision:
                        return ctx.BodyPartHit == BodyPart.Head;
                    case HitCondition.TargetHasStatus:
                        var r = StatusReceiver.For(ctx.Target);
                        if (r == null) return false;
                        for (int i = 0; i < d.statuses.Count; i++) if (r.Has(d.statuses[i])) return true;
                        return false;
                    default:
                        return false;
                }
            }

            // Multiplies the hit's condition multiplier; runs before damage is applied.
            private sealed class Bonus : IHitEffect
            {
                private readonly Runtime owner;
                public Bonus(Runtime owner) => this.owner = owner;
                public EffectPhase Phase => EffectPhase.Modifier;
                public bool PropagatesOnChain => false;
                public void Apply(HitContext ctx, IHitResolver resolver) => ctx.ConditionMultiplier *= 1f + owner.Def.bonus;
            }
        }
    }
}
