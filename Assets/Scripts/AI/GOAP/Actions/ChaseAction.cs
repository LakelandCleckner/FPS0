using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Behaviours;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Actions
{
    [GoapId("f6a7b8c9-d0e1-2345-fabc-456789012345")]
    public class ChaseAction : GoapActionBase<ChaseAction.Data>
    {
        public override void Start(IMonoAgent agent, Data data)
        {
            var brain = agent.Transform.GetComponent<AgentBrain>();
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && brain != null)
                nav.speed = brain.BaseMoveSpeed * brain.ChaseSpeedMultiplier;

        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            // ContinueOrResolve lets GOAP re-evaluate every frame 
            return ActionRunState.ContinueOrResolve;
        }

        public override void Stop(IMonoAgent agent, Data data)
        {
            var brain = agent.Transform.GetComponent<AgentBrain>();
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                if (brain != null)
                    nav.speed = brain.BaseMoveSpeed;
                nav.ResetPath();
            }

        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
        }
    }
}