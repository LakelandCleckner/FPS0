using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using Combat.Enemies;
using GOAPGettingStarted.Behaviours;
using UnityEngine.AI;

namespace GOAPGettingStarted.Actions
{
    // Keeps closing on the player and swings whenever EnemyMeleeAttack allows; the swing holds the action.
    [GoapId("c5bdff00-d286-42e6-89c5-ffcdd0586aad")]
    public class AttackAction : GoapActionBase<AttackAction.Data>
    {
        public override void Start(IMonoAgent agent, Data data)
        {
            data.Melee = agent.Transform.GetComponent<EnemyMeleeAttack>();
            var brain = agent.Transform.GetComponent<AgentBrain>();
            if (brain != null)
                brain.CurrentSpeedMultiplier = brain.ChaseSpeedMultiplier;
        }

        public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
        {
            if (data.Melee == null) return ActionRunState.Completed;

            if (!data.Melee.IsSwinging)
                data.Melee.TryStartAttack();

            // Continue pins the plan for the swing; ContinueOrResolve lets GOAP re-plan between swings.
            return data.Melee.IsSwinging ? ActionRunState.Continue : ActionRunState.ContinueOrResolve;
        }

        public override void Stop(IMonoAgent agent, Data data)
        {
            var brain = agent.Transform.GetComponent<AgentBrain>();
            var nav = agent.Transform.GetComponent<NavMeshAgent>();
            if (brain != null)
                brain.CurrentSpeedMultiplier = 1f;
            if (nav != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
                nav.ResetPath();
        }

        public class Data : IActionData
        {
            public ITarget Target { get; set; }
            public EnemyMeleeAttack Melee;
        }
    }
}
