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
            data.Ranged = agent.Transform.GetComponent<EnemyRangedAttack>();
            var p = data.Brain != null ? data.Brain.Profile : null;
            data.Tolerance = p != null ? p.firingPointTolerance : 1.25f;
            data.Face = p != null && p.faceWhileMoving;
            data.FireWhileWalking = p != null && p.fireWhileWalking;
            data.Firing = false;
            data.Profile = p;
            data.Running = false;
            data.Factor = -1f;
            UpdateGait(agent, data);
        }

        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            return distance <= data.Tolerance;
        }

        // Runs every frame while moving (PerformWhileMoving); done once in range
        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            UpdateGait(agent, data);
            UpdateFacing(data);
            UpdateFiring(data);
            return context.IsInRange ? ActionRunState.Completed : ActionRunState.Continue;
        }

        // Run long moves, walk short ones; 1 m of hysteresis so a move near the threshold doesn't flip gait
        private static void UpdateGait(IMonoAgent agent, Data data)
        {
            if (data.Mover == null) return;
            var p = data.Profile;
            float walk = p != null ? p.walkSpeed : 1.5f;
            float run = p != null ? p.runSpeed : 3.5f;
            float runAt = p != null ? p.runDistance : 6f;

            float left = 0f;
            if (data.Target != null)
            {
                Vector3 d = data.Target.Position - agent.Transform.position;
                d.y = 0f;
                left = d.magnitude;
            }
            if (data.Running ? left < runAt - 1f : left > runAt) data.Running = !data.Running;

            float factor = (data.Running ? run : walk) / Mathf.Max(0.01f, data.Mover.BaseMoveSpeed);
            if (Mathf.Approximately(factor, data.Factor)) return;
            data.Mover.RequestSpeed(data, factor);
            data.Factor = factor;
        }

        // Walking with the target in sight: keep the rifle up and burst on the move
        private static void UpdateFiring(Data data)
        {
            if (data.Ranged == null || data.Perception == null) return;
            bool fire = data.FireWhileWalking && !data.Running && data.Perception.TargetVisible
                        && !data.Ranged.NeedsReload && (data.Brain == null || data.Brain.RangedAllowed);
            if (fire == data.Firing) return;
            data.Firing = fire;
            if (fire) data.Ranged.Engage();
            else data.Ranged.Disengage();
        }

        // Strafe facing a visible target while walking; running faces the way it's going
        private static void UpdateFacing(Data data)
        {
            if (data.Mover == null || data.Perception == null) return;
            if (data.Face && !data.Running && data.Perception.TargetVisible)
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
            if (data.Firing && data.Ranged != null) data.Ranged.Disengage();
            data.Firing = false;
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
            public EnemyBehaviourProfileSO Profile;
            public EnemyRangedAttack Ranged;
            public bool Running;
            public float Factor;
            public bool FireWhileWalking;
            public bool Firing;
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
