using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

namespace Combat.AI.Goap
{
    // Sensors and every goal; each agent type includes it, so the brain can always request them all.
    // A goal no capability can reach is simply never picked.
    public class PerceptionCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Perception");
            b.AddMultiSensor<EnemySensors>();

            b.AddGoal<KillTargetGoal>()
                .SetBaseCost(1)
                .AddCondition<TargetKilled>(Comparison.GreaterThanOrEqual, 1);

            b.AddGoal<StayAliveGoal>()
                .SetBaseCost(1)
                .AddCondition<ShieldRecovered>(Comparison.GreaterThanOrEqual, 1);

            b.AddGoal<SearchGoal>()
                .SetBaseCost(5)
                .AddCondition<Searched>(Comparison.GreaterThanOrEqual, 1);

            b.AddGoal<IdleGoal>()
                .SetBaseCost(10)
                .AddCondition<IsIdle>(Comparison.GreaterThanOrEqual, 1);

            return b.Build();
        }
    }

    // Circle at a distance until a token frees up, then close in and swing.
    public class MeleeCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Melee");

            b.AddAction<WaitForTokenAction>()
                .SetTarget<CombatTarget>()
                .AddCondition<HasTarget>(Comparison.GreaterThanOrEqual, 1)
                .AddCondition<HasAttackToken>(Comparison.SmallerThanOrEqual, 0)
                .AddEffect<HasAttackToken>(EffectType.Increase)
                .SetBaseCost(1)
                .SetValidateConditions(false);

            b.AddAction<MeleeAttackAction>()
                .SetTarget<CombatTarget>()
                .AddCondition<HasTarget>(Comparison.GreaterThanOrEqual, 1)
                .AddCondition<HasAttackToken>(Comparison.GreaterThanOrEqual, 1)
                .AddEffect<TargetKilled>(EffectType.Increase)
                .SetBaseCost(1)
                .SetValidateConditions(false);

            return b.Build();
        }
    }

    // Pick a firing point in the range band with sight of the target, shoot from it, reload when empty.
    public class RangedCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Ranged");

            b.AddAction<MoveToFiringPositionAction>()
                .SetTarget<FiringTarget>()
                .AddCondition<HasTarget>(Comparison.GreaterThanOrEqual, 1)
                .AddCondition<InFiringPosition>(Comparison.SmallerThanOrEqual, 0)
                .AddEffect<InFiringPosition>(EffectType.Increase)
                .SetBaseCost(1)
                .SetMoveMode(ActionMoveMode.PerformWhileMoving)
                .SetValidateConditions(false);

            b.AddAction<ShootTargetAction>()
                .SetTarget<FiringTarget>()
                .AddCondition<HasTarget>(Comparison.GreaterThanOrEqual, 1)
                .AddCondition<InFiringPosition>(Comparison.GreaterThanOrEqual, 1)
                .AddCondition<NeedsReload>(Comparison.SmallerThanOrEqual, 0)
                .AddEffect<TargetKilled>(EffectType.Increase)
                .SetBaseCost(1)
                .SetValidateConditions(false);

            b.AddAction<ReloadAction>()
                .SetTarget<FiringTarget>()
                .SetRequiresTarget(false)
                .AddCondition<NeedsReload>(Comparison.GreaterThanOrEqual, 1)
                .AddEffect<NeedsReload>(EffectType.Decrease)
                .SetBaseCost(1)
                .SetValidateConditions(false);

            return b.Build();
        }
    }

    // Rush the target and self-detonate. No token needed.
    public class KamikazeCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Kamikaze");

            b.AddAction<KamikazeAction>()
                .SetTarget<CombatTarget>()
                .AddCondition<HasTarget>(Comparison.GreaterThanOrEqual, 1)
                .AddEffect<TargetKilled>(EffectType.Increase)
                .SetBaseCost(1)
                .SetValidateConditions(false);

            return b.Build();
        }
    }

    // Shield broke: break off and hide until it recharges.
    public class ShieldCoverCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("ShieldCover");

            b.AddAction<TakeCoverAction>()
                .SetTarget<CoverTarget>()
                .AddCondition<ShieldDown>(Comparison.GreaterThanOrEqual, 1)
                .AddEffect<ShieldRecovered>(EffectType.Increase)
                .SetBaseCost(1)
                .SetInRange(1f)
                .SetValidateConditions(false);

            return b.Build();
        }
    }

    // Walk to the last known position and look around.
    public class SearchCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Search");

            b.AddAction<SearchAction>()
                .SetTarget<LastKnownTarget>()
                .AddCondition<HasLastKnown>(Comparison.GreaterThanOrEqual, 1)
                .AddCondition<HasTarget>(Comparison.SmallerThanOrEqual, 0)
                .AddEffect<Searched>(EffectType.Increase)
                .SetBaseCost(1)
                .SetInRange(2f)
                .SetValidateConditions(false);

            return b.Build();
        }
    }

    // Fallback: wander.
    public class IdleCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Idle");

            b.AddAction<WanderAction>()
                .SetTarget<WanderTarget>()
                .AddEffect<IsIdle>(EffectType.Increase)
                .SetBaseCost(1)
                .SetInRange(1.5f);

            return b.Build();
        }
    }
}
