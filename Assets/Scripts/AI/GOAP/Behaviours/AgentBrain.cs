using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Goals;
using UnityEngine;

namespace GOAPGettingStarted.Behaviours
{
    [RequireComponent(typeof(AgentBehaviour))]
    [RequireComponent(typeof(GoapActionProvider))]
    [RequireComponent(typeof(EnemyMemory))]
    public class AgentBrain : MonoBehaviour
    {
        [Header("Awareness")]
        public float BaseAwarenessDuration = 3f;
        public float ProximityAwarenessBonus = 4f;
        public float ProximityThreshold = 6f;

        private AgentBehaviour agentBehaviour;
        private GoapActionProvider actionProvider;
        private EnemyMemory memory;

        private enum AIState { Normal, Aware, Investigate }
        private AIState state = AIState.Normal;

        private float awarenessTimer = 0f;
        private Transform player;
        private bool wasChasing = false;

        private void Awake()
        {
            agentBehaviour = GetComponent<AgentBehaviour>();
            actionProvider = GetComponent<GoapActionProvider>();
            memory = GetComponent<EnemyMemory>();
        }

        private void Start()
        {
            if (actionProvider.AgentType == null)
            {
                Debug.LogError("[AgentBrain] AgentType is null — check AgentTypeBehaviour Config and Runner are assigned.");
                return;
            }
            if (actionProvider.Receiver == null)
            {
                Debug.LogError("[AgentBrain] Receiver is null — check AgentBehaviour has its Action Provider Base assigned.");
                return;
            }

            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) player = playerObj.transform;

            // Let GOAP handle chase vs wander naturally via conditions
            actionProvider.RequestGoal<ChaseGoal, WanderGoal>();
        }

        private void Update()
        {

            var goalName = actionProvider.CurrentPlan?.Goal?.GetType().Name ?? "None";
            var actionName = agentBehaviour.CurrentAction?.GetType().Name ?? "None";
            Debug.Log($"[AgentBrain] State={state} | Goal={goalName} | Action={actionName}");

            if (actionProvider.AgentType == null) return;

            var isChasing = actionProvider.CurrentPlan?.Goal is ChaseGoal;

            switch (state)
            {
                case AIState.Normal:
                    if (wasChasing && !isChasing)
                    {
                        // Just lost the chase — store last known position and enter aware
                        if (player != null)
                        {
                            memory.LastKnownPlayerPosition = player.position;
                            memory.HasLastKnownPosition = true;
                        }
                        EnterAware();
                    }
                    break;

                case AIState.Aware:
                    if (isChasing)
                    {
                        // Re-acquired player — back to normal
                        state = AIState.Normal;
                        break;
                    }

                    float proximityBonus = 0f;
                    if (player != null && Vector3.Distance(transform.position, player.position) <= ProximityThreshold)
                        proximityBonus = ProximityAwarenessBonus;

                    awarenessTimer -= Time.deltaTime;
                    if (awarenessTimer - proximityBonus <= 0f)
                        EnterInvestigate();
                    break;

                case AIState.Investigate:
                    if (isChasing)
                    {
                        state = AIState.Normal;
                        actionProvider.RequestGoal<ChaseGoal, WanderGoal>();
                        break;
                    }
                    if (!memory.HasLastKnownPosition)
                    {
                        state = AIState.Normal;
                        actionProvider.RequestGoal<ChaseGoal, WanderGoal>();
                    }
                    break;
            }

            wasChasing = isChasing;
        }

        private void EnterAware()
        {
            state = AIState.Aware;
            awarenessTimer = BaseAwarenessDuration;
            // Stay on current goals — GOAP will naturally pick WanderGoal since
            // player is no longer visible, intercept before it settles
        }

        private void EnterInvestigate()
        {
            state = AIState.Investigate;
            actionProvider.RequestGoal<InvestigateGoal>();
        }
    }
}