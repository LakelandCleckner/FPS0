using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;

namespace Combat.AI.Goap
{
    // ---- world keys ----

    // A hostile is known: seen, heard or hit us.
    public class HasTarget : WorldKeyBase { }
    // Line of sight to the target right now.
    public class TargetVisible : WorldKeyBase { }
    // A last known position is waiting to be searched.
    public class HasLastKnown : WorldKeyBase { }
    // Goal keys: always 0, actions raise them.
    public class TargetKilled : WorldKeyBase { }
    public class Searched : WorldKeyBase { }
    public class IsIdle : WorldKeyBase { }

    // ---- target keys ----

    // The live target combatant.
    public class CombatTarget : TargetKeyBase { }
    // Where the target was last seen.
    public class LastKnownTarget : TargetKeyBase { }
    // A random point to wander to.
    public class WanderTarget : TargetKeyBase { }

    // ---- goals ----
    // The planner adds travel distance to goal cost, so lower-priority goals price themselves
    // out while a higher one applies; otherwise a nearby wander point could beat a distant target.

    public class KillTargetGoal : GoalBase { }

    public class SearchGoal : GoalBase
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            var p = references.GetCachedComponent<EnemyPerception>();
            return p != null && p.HasTarget ? Blocked : base.GetCost(agent, references);
        }

        public const float Blocked = 100000f;
    }

    public class IdleGoal : GoalBase
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            var p = references.GetCachedComponent<EnemyPerception>();
            return p != null && (p.HasTarget || p.HasLastKnown) ? SearchGoal.Blocked : base.GetCost(agent, references);
        }
    }
}
