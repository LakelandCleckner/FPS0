using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

namespace Combat.AI.Goap
{
    // Shared sensors every enemy agent type includes.
    public class PerceptionCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Perception");
            b.AddMultiSensor<EnemySensors>();
            return b.Build();
        }
    }

    // Close in and swing.
    public class MeleeCapability : CapabilityFactoryBase
    {
        public override ICapabilityConfig Create()
        {
            var b = new CapabilityBuilder("Melee");

            b.AddGoal<KillTargetGoal>()
                .SetBaseCost(1)
                .AddCondition<TargetKilled>(Comparison.GreaterThanOrEqual, 1);

            b.AddAction<MeleeAttackAction>()
                .SetTarget<CombatTarget>()
                .AddCondition<HasTarget>(Comparison.GreaterThanOrEqual, 1)
                .AddEffect<TargetKilled>(EffectType.Increase)
                .SetBaseCost(1)
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

            b.AddGoal<SearchGoal>()
                .SetBaseCost(5)
                .AddCondition<Searched>(Comparison.GreaterThanOrEqual, 1);

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

            b.AddGoal<IdleGoal>()
                .SetBaseCost(10)
                .AddCondition<IsIdle>(Comparison.GreaterThanOrEqual, 1);

            b.AddAction<WanderAction>()
                .SetTarget<WanderTarget>()
                .AddEffect<IsIdle>(EffectType.Increase)
                .SetBaseCost(1)
                .SetInRange(1.5f);

            return b.Build();
        }
    }
}
