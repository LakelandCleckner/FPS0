using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Actions
{
    [GoapId("2b98bd06-6921-455f-aaae-01cb5d1a5f5c")]
    public class WanderAction : GoapActionBase<WanderAction.Data>
    {
        public float MinDuration = 3f;
        public float MaxDuration = 6f;

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Timer = Random.Range(MinDuration, MaxDuration);
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            data.Timer -= context.DeltaTime;
            return data.Timer <= 0f ? ActionRunState.Completed : ActionRunState.Continue;
        }

        public override void Stop(IMonoAgent agent, Data data)
        {
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (nav != null) nav.ResetPath();
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public float Timer { get; set; }
        }
    }
}