using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using Combat.Enemies;

namespace Combat.AI.Goap
{
    // Walk to the brain's firing point (or advance point). Faces a visible target on the way when the profile says so.
    public class MoveToFiringPositionAction : GoapActionBase<MoveToFiringPositionAction.Data>
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references, ITarget target)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            return b != null && !b.RangedAllowed ? SearchGoal.Blocked : base.GetCost(agent, references, target);
        }

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Brain = agent.Transform.GetComponent<EnemyBrain>();
            data.Perception = agent.Transform.GetComponent<EnemyPerception>();
            var p = data.Brain != null ? data.Brain.Profile : null;
            data.Tolerance = p != null ? p.firingPointTolerance : 1.25f;
            data.Face = p != null && p.faceWhileMoving;
            if (data.Mover != null) data.Mover.RequestSpeed(data, p != null ? p.advanceSpeedFactor : 1f);
        }

        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            return distance <= data.Tolerance;
        }

        // Runs every frame while moving (PerformWhileMoving); done once in range
        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            UpdateFacing(data);
            return context.IsInRange ? ActionRunState.Completed : ActionRunState.Continue;
        }

        // Keep the target in view while walking, if it's visible
        private static void UpdateFacing(Data data)
        {
            if (data.Mover == null || data.Perception == null) return;
            if (data.Face && data.Perception.TargetVisible)
            {
                data.Mover.RequestLook(data, data.Perception.BelievedPosition + Vector3.up * 1.4f);
                data.Mover.RequestFace(data);
            }
            else
            {
                data.Mover.ReleaseFace(data);
                data.Mover.ReleaseLook(data);
            }
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover == null) return;
            data.Mover.ReleaseFace(data);
            data.Mover.ReleaseLook(data);
            data.Mover.ReleaseSpeed(data);
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public EnemyBrain Brain;
            public EnemyPerception Perception;
            public float Tolerance;
            public bool Face;
        }
    }

    // Hold the firing point facing the believed target while EnemyRangedAttack bursts; stops when the point, ammo or target goes.
    public class ShootTargetAction : GoapActionBase<ShootTargetAction.Data>
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references, ITarget target)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            return b != null && !b.RangedAllowed ? SearchGoal.Blocked : base.GetCost(agent, references, target);
        }

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Brain = agent.Transform.GetComponent<EnemyBrain>();
            data.Perception = agent.Transform.GetComponent<EnemyPerception>();
            data.Ranged = agent.Transform.GetComponent<EnemyRangedAttack>();
            var p = data.Brain != null ? data.Brain.Profile : null;
            data.Tolerance = p != null ? p.firingPointTolerance : 1.25f;
            data.Engaged = false;
        }

        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            return (b != null && b.InFiringPosition) || distance <= data.Tolerance;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Ranged == null || data.Brain == null || data.Perception == null) return ActionRunState.Stop;
            if (!data.Perception.HasTarget) return ActionRunState.Completed;
            if (!data.Brain.InFiringPosition || data.Ranged.NeedsReload || !data.Brain.RangedAllowed) return ActionRunState.Stop;

            if (!data.Engaged)
            {
                data.Engaged = true;
                data.Mover.Halt();
                data.Mover.RequestFace(data);
                data.Ranged.Engage();
                data.Brain.Raise(EnemyDecision.Engaging);
            }

            data.Mover.RequestLook(data, data.Perception.BelievedPosition + Vector3.up * 1.4f);

            // A burst pins the plan; between bursts GOAP may pick something better
            return data.Ranged.CurrentPhase == EnemyRangedAttack.Phase.Bursting
                ? ActionRunState.Continue : ActionRunState.ContinueOrResolve;
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Ranged != null) data.Ranged.Disengage();
            if (data.Brain != null) data.Brain.ReleaseRangedToken();
            if (data.Mover == null) return;
            data.Mover.ReleaseFace(data);
            data.Mover.ReleaseLook(data);
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public EnemyBrain Brain;
            public EnemyPerception Perception;
            public EnemyRangedAttack Ranged;
            public float Tolerance;
            public bool Engaged;
        }
    }

    // Reload in place; the reload clip is timed to ranged_reload_time.
    public class ReloadAction : GoapActionBase<ReloadAction.Data>
    {
        public override void Start(IMonoAgent agent, Data data)
        {
            data.Ranged = agent.Transform.GetComponent<EnemyRangedAttack>();
            data.Brain = agent.Transform.GetComponent<EnemyBrain>();
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Perception = agent.Transform.GetComponent<EnemyPerception>();
            if (data.Ranged != null && data.Ranged.StartReload() && data.Brain != null) data.Brain.Raise(EnemyDecision.Reloading);
        }

        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references) => true;

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Ranged == null) return ActionRunState.Stop;
            if (data.Mover != null && data.Perception != null && data.Perception.HasTarget)
                data.Mover.RequestLook(data, data.Perception.BelievedPosition + Vector3.up * 1.4f);
            if (data.Ranged.IsReloading) return ActionRunState.Continue;
            return data.Ranged.NeedsReload ? ActionRunState.Stop : ActionRunState.Completed;
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover != null) data.Mover.ReleaseLook(data);
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyRangedAttack Ranged;
            public EnemyBrain Brain;
            public EnemyMover Mover;
            public EnemyPerception Perception;
        }
    }
}
