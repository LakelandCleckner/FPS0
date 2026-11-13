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
    // Holds one of the target's melee attack tokens.
    public class HasAttackToken : WorldKeyBase { }
    // Shield broke and hasn't recharged.
    public class ShieldDown : WorldKeyBase { }
    public class TargetKilled : WorldKeyBase { }
    public class ShieldRecovered : WorldKeyBase { }
    public class Searched : WorldKeyBase { }
    public class IsIdle : WorldKeyBase { }
    // At a firing point that has sight of the believed target position.
    public class InFiringPosition : WorldKeyBase { }
    // Magazine empty.
    public class NeedsReload : WorldKeyBase { }
    // Holds one of the target's ranged attack tokens.
    public class HasRangedToken : WorldKeyBase { }
    // The brain picked cover in the range band to fight from.
    public class HasCombatCover : WorldKeyBase { }
    // At (or beside) that cover.
    public class InCover : WorldKeyBase { }
    // DefensiveBand as an int: 0 aggressive, 1 normal, 2 defensive.
    public class Defensive : WorldKeyBase { }
    // CombatStatus as an int: 0 idle .. 5 danger.
    public class Awareness : WorldKeyBase { }

    // ---- target keys ----

    // The live target combatant.
    public class CombatTarget : TargetKeyBase { }
    // Where the target was last seen.
    public class LastKnownTarget : TargetKeyBase { }
    // A random point to wander to.
    public class WanderTarget : TargetKeyBase { }
    // Somewhere out of the target's sight.
    public class CoverTarget : TargetKeyBase { }
    // Where to shoot from (or an advance point when nowhere has sight).
    public class FiringTarget : TargetKeyBase { }
    // Cover to fight from.
    public class CombatCoverTarget : TargetKeyBase { }

    // ---- goals ----
    // The planner adds travel distance to goal cost, so lower-priority goals price themselves
    // out while a higher one applies; otherwise a nearby wander point could beat a distant target.

    public class KillTargetGoal : GoalBase
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            return b != null && b.WantsCover ? SearchGoal.Blocked : base.GetCost(agent, references);
        }
    }

    // Shield broke: get out of sight until it's back. Only applies while it wants cover.
    public class StayAliveGoal : GoalBase
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            var p = references.GetCachedComponent<EnemyPerception>();
            return b != null && b.WantsCover && p != null && p.HasTarget ? base.GetCost(agent, references) : SearchGoal.Blocked;
        }
    }

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
