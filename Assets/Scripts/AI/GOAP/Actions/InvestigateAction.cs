using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Behaviours;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Actions
{
    [GoapId("d4e5f6a7-b8c9-0123-defa-012345678901")]
    public class InvestigateAction : GoapActionBase<InvestigateAction.Data>
    {
        public float ArrivalDistance = 2f;
        public float LookAroundDuration = 3f;
        public float LookRotationSpeed = 90f;
        public float LookIntervalMin = 0.8f;
        public float LookIntervalMax = 1.5f;

        public override void Start(IMonoAgent agent, Data data)
        {
            var memory = agent.Transform.GetComponent<EnemyMemory>();
            if (memory != null)
            {
                data.Destination = memory.LastKnownPlayerPosition;
                data.LookAroundTimer = 0f;
                data.Arrived = false;
                data.LookTimer = 0f;
                data.TargetLookAngle = agent.Transform.eulerAngles.y;
            }

            var brain = agent.Transform.GetComponent<AgentBrain>();
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                if (brain != null)
                    nav.speed = brain.BaseMoveSpeed * brain.InvestigateSpeedMultiplier;
                nav.SetDestination(data.Destination);
            }
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (!data.Arrived)
            {
                if (Vector3.Distance(agent.Transform.position, data.Destination) <= ArrivalDistance)
                {
                    data.Arrived = true;
                    data.LookAroundTimer = LookAroundDuration;
                    data.LookTimer = 0f;

                    var nav = agent.Transform.GetComponent<NavMeshAgent>();
                    if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
                    {
                        nav.ResetPath();
                        nav.updateRotation = false;
                    }
                }
                return ActionRunState.ContinueOrResolve;
            }

            data.LookAroundTimer -= context.DeltaTime;

            data.LookTimer -= context.DeltaTime;
            if (data.LookTimer <= 0f)
            {
                data.TargetLookAngle = Random.Range(0f, 360f);
                data.LookTimer = Random.Range(LookIntervalMin, LookIntervalMax);
            }

            var current = agent.Transform.eulerAngles.y;
            var next = Mathf.MoveTowardsAngle(current, data.TargetLookAngle, LookRotationSpeed * context.DeltaTime);
            agent.Transform.rotation = Quaternion.Euler(0f, next, 0f);

            if (data.LookAroundTimer <= 0f)
            {
                var nav = agent.Transform.GetComponent<NavMeshAgent>();
                if (nav != null && nav.isActiveAndEnabled)
                    nav.updateRotation = true;

                var memory = agent.Transform.GetComponent<EnemyMemory>();
                if (memory != null)
                    memory.HasLastKnownPosition = false;

                return ActionRunState.Completed;
            }

            return ActionRunState.ContinueOrResolve;
        }

        public override void Stop(IMonoAgent agent, Data data)
        {
            var brain = agent.Transform.GetComponent<AgentBrain>();
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled)
            {
                nav.updateRotation = true;
                if (brain != null)
                    nav.speed = brain.BaseMoveSpeed;
                if (nav.isOnNavMesh)
                    nav.ResetPath();
            }
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public Vector3 Destination { get; set; }
            public float LookAroundTimer { get; set; }
            public bool Arrived { get; set; }
            public float LookTimer { get; set; }
            public float TargetLookAngle { get; set; }
        }
    }
}