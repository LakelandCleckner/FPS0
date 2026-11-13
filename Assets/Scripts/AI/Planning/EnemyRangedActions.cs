using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;
using Combat.Enemies;

namespace Combat.AI.Goap
{
    // Walk to the brain's firing point (or advance point). Faces a visible target on the way when the profile says so.
    public class MoveToFiringPositionAction : GoapActionBase<MoveToFiringPositionAction.Data>
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references, ITarget target)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            if (b != null && !b.RangedAllowed) return SearchGoal.Blocked;
            float cost = base.GetCost(agent, references, target);
            return b != null ? cost + b.OpenShootingExtraCost : cost;
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
            float pace = data.Brain != null ? data.Brain.Tactics.speedScale : 1f;
            float walk = (p != null ? p.walkSpeed : 1.5f) * pace;
            float run = (p != null ? p.runSpeed : 3.5f) * pace;
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
            if (b != null && !b.RangedAllowed) return SearchGoal.Blocked;
            float cost = base.GetCost(agent, references, target);
            return b != null ? cost + b.OpenShootingExtraCost : cost;
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
            data.StrafeOffset = 0f;
            data.NextStrafe = Time.time + (p != null ? StrafeInterval(data.Brain) : 2f);
            if (data.Mover != null && p != null)
                data.Mover.RequestSpeed(data, p.walkSpeed * data.Brain.Tactics.speedScale / Mathf.Max(0.01f, data.Mover.BaseMoveSpeed));
        }

        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            return (b != null && b.InFiringArea) || distance <= data.Tolerance;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Ranged == null || data.Brain == null || data.Perception == null) return ActionRunState.Stop;
            if (!data.Perception.HasTarget) return ActionRunState.Completed;
            if (!data.Brain.InFiringArea || data.Ranged.NeedsReload || !data.Brain.RangedAllowed) return ActionRunState.Stop;

            if (!data.Engaged)
            {
                data.Engaged = true;
                data.Mover.RequestPoint(data, data.Brain.FiringPoint);
                data.Mover.RequestFace(data);
                data.Ranged.Engage();
                data.Brain.Raise(EnemyDecision.Engaging);
            }

            data.Mover.RequestLook(data, data.Perception.BelievedPosition + Vector3.up * 1.4f);
            UpdateStrafe(data);

            // A burst pins the plan; between bursts GOAP may pick something better
            return data.Ranged.CurrentPhase == EnemyRangedAttack.Phase.Bursting
                ? ActionRunState.Continue : ActionRunState.ContinueOrResolve;
        }

        private const float MinStrafeStep = 0.8f;

        private static float StrafeInterval(EnemyBrain b)
        {
            var p = b.Profile;
            return Random.Range(p.strafeIntervalMin, Mathf.Max(p.strafeIntervalMin, p.strafeIntervalMax)) * b.Tactics.strafeIntervalScale;
        }

        // Every few seconds sidestep to a new spot beside the firing point, across the line to the target, that still sees it
        private static void UpdateStrafe(Data data)
        {
            var p = data.Brain.Profile;
            if (p == null || !p.strafe || !data.Perception.TargetVisible || data.Ranged.IsReloading) return;
            float now = Time.time;
            if (now < data.NextStrafe) return;
            data.NextStrafe = now + StrafeInterval(data.Brain);

            Vector3 anchor = data.Brain.FiringPoint;
            Vector3 threat = data.Perception.BelievedPosition;
            Vector3 to = threat - anchor;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            Vector3 side = Vector3.Cross(Vector3.up, to.normalized);

            for (int i = 0; i < 3; i++)
            {
                float off = Random.Range(-p.strafeDistance, p.strafeDistance);
                if (Mathf.Abs(off - data.StrafeOffset) < MinStrafeStep) continue;
                if (!NavMesh.SamplePosition(anchor + side * off, out var hit, 0.5f, NavMesh.AllAreas)) continue;
                if (NavMesh.Raycast(anchor, hit.position, out _, NavMesh.AllAreas)) continue;
                if (!PositionScorer.HasLineOfSight(hit.position, threat)) continue;
                data.StrafeOffset = off;
                data.Mover.RequestPoint(data, hit.position);
                return;
            }
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Ranged != null) data.Ranged.Disengage();
            if (data.Brain != null) data.Brain.ReleaseRangedToken();
            if (data.Mover == null) return;
            data.Mover.ReleasePoint(data);
            data.Mover.ReleaseSpeed(data);
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
            public float StrafeOffset;
            public float NextStrafe;
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
            data.Animator = agent.Transform.GetComponentInChildren<EnemyShooterAnimator>();
            if (data.Ranged != null && data.Ranged.StartReload() && data.Brain != null) data.Brain.Raise(EnemyDecision.Reloading);
            // Behind half cover: get down for it
            if (data.Animator != null && data.Brain != null && data.Brain.InCover && data.Brain.CombatCover.Half) data.Animator.Crouched = true;
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
            if (data.Animator != null) data.Animator.Crouched = false;
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyRangedAttack Ranged;
            public EnemyBrain Brain;
            public EnemyMover Mover;
            public EnemyPerception Perception;
            public EnemyShooterAnimator Animator;
        }
    }

    // Walk or run to the brain's cover spot (same gait and fire-on-the-move as firing points), arriving exactly on it.
    public class MoveToCoverAction : MoveToFiringPositionAction
    {
        public override float GetCost(IActionReceiver agent, IComponentReference references, ITarget target)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            if (b == null || !b.RangedAllowed || !b.HasCombatCover) return SearchGoal.Blocked;
            return 1f + b.CoverExtraCost;
        }

        public override void Start(IMonoAgent agent, Data data)
        {
            base.Start(agent, data);
            data.Tolerance = EnemyBrain.CoverTolerance;
            Steer(data);
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Brain == null || !data.Brain.HasCombatCover) return ActionRunState.Stop;
            Steer(data);
            return base.Perform(agent, data, context);
        }

        // The cover can move under us (re-picked): follow it
        private static void Steer(Data data)
        {
            if (data.Mover != null && data.Brain != null && data.Brain.HasCombatCover)
                data.Mover.RequestPoint(data, data.Brain.CombatCover.Point);
        }

        public override void End(IMonoAgent agent, Data data)
        {
            base.End(agent, data);
            if (data.Mover != null) data.Mover.ReleasePoint(data);
        }
    }

    // At cover: hide (crouched behind half cover), then with a ranged token stand or step out to the peek spot,
    // fire a burst, and duck back. Reload happens hidden. Leaves when the cover stops hiding or gets re-picked.
    public class PeekAndShootAction : GoapActionBase<PeekAndShootAction.Data>
    {
        internal enum State { Hide, Peek, Return }

        private const float FirstHideMin = 0.2f;
        private const float FirstHideMax = 0.6f;
        private const float PeekArrive = 0.3f;

        public override float GetCost(IActionReceiver agent, IComponentReference references, ITarget target)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            if (b == null || !b.RangedAllowed || !b.HasCombatCover) return SearchGoal.Blocked;
            return 1f + b.CoverExtraCost;
        }

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Brain = agent.Transform.GetComponent<EnemyBrain>();
            data.Perception = agent.Transform.GetComponent<EnemyPerception>();
            data.Ranged = agent.Transform.GetComponent<EnemyRangedAttack>();
            data.Animator = agent.Transform.GetComponentInChildren<EnemyShooterAnimator>();
            data.Profile = data.Brain != null ? data.Brain.Profile : null;
            data.Spot = data.Brain != null ? data.Brain.CombatCover : default;
            if (data.Mover != null)
            {
                data.Mover.RequestPoint(data, data.Spot.Point);
                data.Mover.RequestFace(data);
            }
            EnterHide(data, Random.Range(FirstHideMin, FirstHideMax));
        }

        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            var b = references.GetCachedComponent<EnemyBrain>();
            return b != null && b.InCover;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            var b = data.Brain;
            if (b == null || data.Ranged == null || data.Perception == null || data.Mover == null) return ActionRunState.Stop;
            if (!data.Perception.HasTarget) return ActionRunState.Completed;
            if (!b.HasCombatCover || !b.RangedAllowed) return ActionRunState.Stop;

            // Re-picked cover somewhere else: go there
            if ((b.CombatCover.Point - data.Spot.Point).sqrMagnitude > EnemyBrain.CoverTolerance * EnemyBrain.CoverTolerance)
                return ActionRunState.Stop;
            data.Spot = b.CombatCover;

            data.Mover.RequestLook(data, data.Perception.BelievedPosition + Vector3.up * 1.4f);
            float now = Time.time;

            switch (data.State)
            {
                case State.Hide:
                    // Get down once actually behind the cover, not while still walking in
                    SetCrouch(data, data.Spot.Half && data.Mover.AtPoint(data.Spot.Point, EnemyBrain.CoverTolerance));
                    if (data.Ranged.NeedsReload) return ActionRunState.Stop;   // reload here, hidden
                    if (now < data.HideUntil) return ActionRunState.ContinueOrResolve;
                    // Only step out holding a ranged token, so the group takes turns
                    if (!b.HasRangedToken && !b.AcquireRangedToken()) return ActionRunState.ContinueOrResolve;
                    EnterPeek(data, now);
                    return ActionRunState.Continue;

                case State.Peek:
                    if (data.Spot.Half) SetCrouch(data, false);
                    else data.Mover.RequestPoint(data, data.Spot.Peek);

                    if (data.Spot.Half || data.Mover.AtPoint(data.Spot.Peek, PeekArrive)) data.Ranged.Engage();

                    var phase = data.Ranged.CurrentPhase;
                    if (phase == EnemyRangedAttack.Phase.Bursting) data.SawBurst = true;
                    else if (data.SawBurst) { data.SawBurst = false; data.BurstsLeft--; }

                    if (data.BurstsLeft <= 0 || data.Ranged.NeedsReload || now - data.PeekStart > PeekMax(data))
                        EnterReturn(data);
                    return ActionRunState.Continue;

                default:
                    if (data.Spot.Half || data.Mover.AtPoint(data.Spot.Point, EnemyBrain.CoverTolerance))
                        EnterHide(data, Random.Range(HideMin(data), HideMax(data)));
                    return ActionRunState.Continue;
            }
        }

        private static void EnterHide(Data data, float seconds)
        {
            data.State = State.Hide;
            data.HideUntil = Time.time + seconds;
        }

        private static void EnterPeek(Data data, float now)
        {
            data.State = State.Peek;
            data.PeekStart = now;
            data.SawBurst = false;
            data.BurstsLeft = (data.Profile != null ? Mathf.Max(1, data.Profile.peekBursts) : 1) + data.Brain.Tactics.extraPeekBursts;
            data.Brain.Raise(EnemyDecision.Peeking);
        }

        // Stop shooting, free the token for the next shooter, get back behind the cover
        private static void EnterReturn(Data data)
        {
            data.State = State.Return;
            data.Ranged.Disengage();
            data.Brain.ReleaseRangedToken();
            data.Mover.RequestPoint(data, data.Spot.Point);
        }

        private static void SetCrouch(Data data, bool on)
        {
            if (data.Animator != null) data.Animator.Crouched = on;
        }

        private static float HideMin(Data d) => (d.Profile != null ? d.Profile.hideMin : 0.8f) * d.Brain.Tactics.hideScale;
        private static float HideMax(Data d) => (d.Profile != null ? Mathf.Max(d.Profile.hideMin, d.Profile.hideMax) : 2f) * d.Brain.Tactics.hideScale;
        private static float PeekMax(Data d) => d.Profile != null ? d.Profile.peekMaxTime : 3f;

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Ranged != null) data.Ranged.Disengage();
            if (data.Brain != null) data.Brain.ReleaseRangedToken();
            SetCrouch(data, false);
            if (data.Mover == null) return;
            data.Mover.ReleasePoint(data);
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
            public EnemyShooterAnimator Animator;
            public EnemyBehaviourProfileSO Profile;
            public CoverSpot Spot;
            internal State State;
            public float HideUntil;
            public float PeekStart;
            public bool SawBurst;
            public int BurstsLeft;
        }
    }
}
