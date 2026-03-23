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
        public float LookAroundDuration = 2f;
        public override void Start(IMonoAgent agent, Data data)
        {
            var memory = agent.Transform.GetComponent<EnemyMemory>();
            if (memory != null)
            {
                data.Destination = memory.LastKnownPlayerPosition;
                data.LookAroundTimer = 0f;
                data.Arrived = false;
            }
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
                nav.SetDestination(data.Destination);
        }
        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (!data.Arrived)
            {
                if (Vector3.Distance(agent.Transform.position, data.Destination) <= ArrivalDistance)
                {
                    data.Arrived = true;
                    data.LookAroundTimer = LookAroundDuration;
                    var nav = agent.Transform.GetComponent<NavMeshAgent>();
                    if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
                        nav.ResetPath();
                }
                return ActionRunState.Continue;
            }
            data.LookAroundTimer -= context.DeltaTime;
            if (data.LookAroundTimer <= 0f)
            {
                var memory = agent.Transform.GetComponent<EnemyMemory>();
                if (memory != null) memory.HasLastKnownPosition = false;
                return ActionRunState.Completed;
            }
            return ActionRunState.Continue;
        }
        public override void Stop(IMonoAgent agent, Data data)
        {
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
                nav.ResetPath();
        }
        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public Vector3 Destination { get; set; }
            public float LookAroundTimer { get; set; }
            public bool Arrived { get; set; }
        }
    }
}