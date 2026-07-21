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

            // Declare the speed multiplier; AgentMoveBehaviour applies nav.speed
            // (base move_speed resolved from stats x this multiplier).
            var brain = agent.Transform.GetComponent<AgentBrain>();
            if (brain != null)
                brain.CurrentSpeedMultiplier = brain.InvestigateSpeedMultiplier;

            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                // Ensure we start under normal agent rotation even if a previous
                // run was interrupted mid-look-around.
                nav.updateRotation = true;
                nav.SetDestination(data.Destination);
            }
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (!data.Arrived)
            {
                /*if (Time.frameCount % 60 == 0)
                {
                    Vector3 d = data.Destination - agent.Transform.position; d.y = 0f;
                    Debug.Log($"[Inv] flatDist={d.magnitude:F2} arrive={ArrivalDistance} navRemaining={agent.Transform.GetComponent<UnityEngine.AI.NavMeshAgent>().remainingDistance:F2}");
                }*/

                if (Vector3.Distance(agent.Transform.position, data.Destination) <= ArrivalDistance)
                {
                    data.Arrived = true;
                    data.LookAroundTimer = LookAroundDuration;
                    data.LookTimer = 0f;

                    var nav = agent.Transform.GetComponent<NavMeshAgent>();
                    if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
                    {
                        nav.ResetPath();
                        // hand rotation over to us for the look-around
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
            if (brain != null)
                brain.CurrentSpeedMultiplier = 1f;

            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled)
            {
                // always hand rotation back, whatever stage we were interrupted at
                nav.updateRotation = true;
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