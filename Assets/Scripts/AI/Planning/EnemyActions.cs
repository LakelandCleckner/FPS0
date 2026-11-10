using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using Combat.Enemies;

namespace Combat.AI.Goap
{
    // With a token: chase the target (moving phase) and swing whenever EnemyMeleeAttack allows; a swing holds the plan.
    public class MeleeAttackAction : GoapActionBase<MeleeAttackAction.Data>
    {
        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Melee = agent.Transform.GetComponent<EnemyMeleeAttack>();
            if (data.Mover != null) data.Mover.RequestSpeed(data, data.Mover.ChaseFactor);
        }

        // In range at attack_range, or while a swing is still playing out.
        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            var melee = references.GetCachedComponent<EnemyMeleeAttack>();
            if (melee == null) return false;
            return melee.IsSwinging || distance <= melee.Range;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Melee == null) return ActionRunState.Completed;
            if (!data.Melee.IsSwinging) data.Melee.TryStartAttack();

            // Continue pins the plan for the swing; ContinueOrResolve lets GOAP re-plan between swings.
            return data.Melee.IsSwinging ? ActionRunState.Continue : ActionRunState.ContinueOrResolve;
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover == null) return;
            data.Mover.ReleaseSpeed(data);
            data.Mover.Halt();
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public EnemyMeleeAttack Melee;
        }
    }

    // Circle the target at the profile's hold distance until one of its attack tokens frees up.
    public class WaitForTokenAction : GoapActionBase<WaitForTokenAction.Data>
    {
        private const float RingSlack = 2f;

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Brain = agent.Transform.GetComponent<EnemyBrain>();
            data.Perception = agent.Transform.GetComponent<EnemyPerception>();
            var p = data.Brain != null ? data.Brain.Profile : null;
            data.HoldDistance = p != null ? p.holdDistance : 5f;
            if (data.Mover != null)
            {
                data.Mover.RequestSpeed(data, data.Mover.ChaseFactor);
                data.Mover.Hold(data, data.HoldDistance, p != null ? p.holdCircleSpeed : 12f);
            }
        }

        // Takes a free token on the way in; otherwise performs once on the ring.
        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            return TryAcquire(data) || distance <= data.HoldDistance + RingSlack;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            return TryAcquire(data) ? ActionRunState.Completed : ActionRunState.ContinueOrResolve;
        }

        private static bool TryAcquire(Data data)
        {
            if (data.Brain == null || data.Perception == null) return false;
            return AttackTokens.TryAcquire(data.Perception.Target, data.Brain);
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover == null) return;
            data.Mover.ReleaseHold(data);
            data.Mover.ReleaseSpeed(data);
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public EnemyBrain Brain;
            public EnemyPerception Perception;
            public float HoldDistance;
        }
    }

    // Sprint at the target and self-detonate in range; the death effects do the damage.
    public class KamikazeAction : GoapActionBase<KamikazeAction.Data>
    {
        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Health = agent.Transform.GetComponent<CombatantHealth>();
            var brain = agent.Transform.GetComponent<EnemyBrain>();
            var p = brain != null ? brain.Profile : null;
            data.TriggerRange = p != null ? p.kamikazeTriggerRange : 2.5f;
            if (data.Mover != null) data.Mover.RequestSpeed(data, p != null ? p.kamikazeSpeedFactor : data.Mover.ChaseFactor);
        }

        public override bool IsInRange(IMonoAgent agent, float distance, Data data, IComponentReference references)
        {
            return distance <= data.TriggerRange;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Health != null) data.Health.Kill();
            return ActionRunState.Completed;
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover != null) data.Mover.ReleaseSpeed(data);
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public CombatantHealth Health;
            public float TriggerRange;
        }
    }

    // Break off to cover (or back away) and wait there until the shield is full again.
    public class TakeCoverAction : GoapActionBase<TakeCoverAction.Data>
    {
        private const float DriftLimit = 1.5f;

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Brain = agent.Transform.GetComponent<EnemyBrain>();
            if (data.Brain != null) data.Brain.ReleaseToken();
            if (data.Mover != null) data.Mover.RequestSpeed(data, data.Mover.ChaseFactor);
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Brain == null || !data.Brain.ShieldDown) return ActionRunState.Completed;

            // Cover got exposed and a new spot was picked: re-plan so we move there
            if (data.Target != null && Vector3.Distance(agent.Transform.position, data.Target.Position) > DriftLimit)
                return ActionRunState.Stop;

            if (data.Mover != null) data.Mover.Halt();
            return ActionRunState.ContinueOrResolve;
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover == null) return;
            data.Mover.ReleaseSpeed(data);
            data.Mover.Halt();
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public EnemyBrain Brain;
        }
    }

    // Walk to the last known position, then look around toward where the target was heading.
    public class SearchAction : GoapActionBase<SearchAction.Data>
    {
        private const float LookAroundDuration = 3f;
        private const float LookRotationSpeed = 90f;
        private const float LookIntervalMin = 0.8f;
        private const float LookIntervalMax = 1.5f;
        // Look-around stays within this many degrees either side of the target's heading.
        private const float LookSpread = 90f;

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            data.Perception = agent.Transform.GetComponent<EnemyPerception>();
            data.Arrived = false;
            data.LookTimer = 0f;
            data.LookAroundTimer = 0f;
            data.TargetLookAngle = agent.Transform.eulerAngles.y;

            var h = data.Perception != null ? data.Perception.LastKnownHeading : Vector3.zero;
            data.HeadingAngle = h.sqrMagnitude > 0.01f ? Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg : agent.Transform.eulerAngles.y;

            if (data.Mover != null) data.Mover.RequestSpeed(data, data.Mover.InvestigateFactor);
        }

        // Runs once arrived (the GOAP agent moves us there first).
        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (!data.Arrived)
            {
                data.Arrived = true;
                data.LookAroundTimer = LookAroundDuration;
                data.TargetLookAngle = data.HeadingAngle;
                data.LookTimer = Random.Range(LookIntervalMin, LookIntervalMax);
                if (data.Mover != null) { data.Mover.Halt(); data.Mover.SetManualRotation(true); }
            }

            data.LookAroundTimer -= context.DeltaTime;
            data.LookTimer -= context.DeltaTime;
            if (data.LookTimer <= 0f)
            {
                data.TargetLookAngle = data.HeadingAngle + Random.Range(-LookSpread, LookSpread);
                data.LookTimer = Random.Range(LookIntervalMin, LookIntervalMax);
            }

            float y = Mathf.MoveTowardsAngle(agent.Transform.eulerAngles.y, data.TargetLookAngle, LookRotationSpeed * context.DeltaTime);
            agent.Transform.rotation = Quaternion.Euler(0f, y, 0f);

            if (data.LookAroundTimer > 0f) return ActionRunState.ContinueOrResolve;

            if (data.Perception != null) data.Perception.ClearLastKnown();
            return ActionRunState.Completed;
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover == null) return;
            data.Mover.SetManualRotation(false);
            data.Mover.ReleaseSpeed(data);
            data.Mover.Halt();
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public EnemyPerception Perception;
            public bool Arrived;
            public float LookAroundTimer;
            public float LookTimer;
            public float TargetLookAngle;
            public float HeadingAngle;
        }
    }

    // Walk to a random point and idle there for a while.
    public class WanderAction : GoapActionBase<WanderAction.Data>
    {
        private const float MinDuration = 3f;
        private const float MaxDuration = 10f;

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Timer = Random.Range(MinDuration, MaxDuration);
            data.Mover = agent.Transform.GetComponent<EnemyMover>();
            var brain = agent.Transform.GetComponent<EnemyBrain>();
            if (brain != null) brain.WanderReroll = true;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            data.Timer -= context.DeltaTime;
            return data.Timer <= 0f ? ActionRunState.Completed : ActionRunState.ContinueOrResolve;
        }

        public override void End(IMonoAgent agent, Data data)
        {
            if (data.Mover != null) data.Mover.Halt();
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMover Mover;
            public float Timer;
        }
    }
}
