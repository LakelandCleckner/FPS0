using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Behaviours;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Actions
{
    [GoapId("2b98bd06-6921-455f-aaae-01cb5d1a5f5c")]
    public class WanderAction : GoapActionBase<WanderAction.Data>
    {
        public float MinDuration = 3f;
        public float MaxDuration = 10f;

        public override void Start(IMonoAgent agent, Data data)
        {
            data.Timer = Random.Range(MinDuration, MaxDuration);

            // TRUE WANDER: every time a wander begins, force the sensor to pick a fresh
            // point rather than resuming a held one. Without this, an interrupted wander
            // (chase, evade, death+respawn) resumes the exact same target, which reads
            // as patrol behaviour. The marker carries the signal to the sensor, which
            // has no way to see this action's Data directly.
            var marker = agent.Transform.GetComponent<WanderMarker>();
            if (marker != null)
                marker.PickFreshTarget = true;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            data.Timer -= context.DeltaTime;
            return data.Timer <= 0f ? ActionRunState.Completed : ActionRunState.ContinueOrResolve;
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
            public float Timer { get; set; }
        }
    }
}