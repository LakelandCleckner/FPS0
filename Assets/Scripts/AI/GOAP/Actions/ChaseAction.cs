using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Actions
{
    [GoapId("f6a7b8c9-d0e1-2345-fabc-456789012345")]
    public class ChaseAction : GoapActionBase<ChaseAction.Data>
    {
        public float ChaseSpeed = 5f;

        public override void Start(IMonoAgent agent, Data data)
        {
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null)
                nav.speed = ChaseSpeed;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            // ContinueOrResolve lets GOAP re-evaluate every frame so it
            // immediately switches back to wander when player leaves FOV
            return ActionRunState.ContinueOrResolve;
        }

        public override void Stop(IMonoAgent agent, Data data)
        {
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                nav.speed = 3.5f;
                nav.ResetPath();
            }
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
        }
    }
}