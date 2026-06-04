using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Goals;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Behaviours
{
    [RequireComponent(typeof(AgentBehaviour))]
    [RequireComponent(typeof(GoapActionProvider))]
    [RequireComponent(typeof(EnemyMemory))]
    public class AgentBrain : MonoBehaviour
    {
        [Header("Detection")]
        public float DetectionRange = 25f;
        public float FieldOfViewAngle = 120f;

        [Header("Awareness")]
        public float BaseAwarenessDuration = 3f;
        public float ProximityAwarenessBonus = 4f;
        public float ProximityThreshold = 6f;

        [Header("Movement")]
        public float BaseMoveSpeed = 3.5f;
        public float ChaseSpeedMultiplier = 1.6f;
        public float InvestigateSpeedMultiplier = 1.3f;

        private AgentBehaviour agentBehaviour;
        private GoapActionProvider actionProvider;
        private EnemyMemory memory;

        private enum AIState { Normal, Aware, Investigate }
        private AIState state = AIState.Normal;

        private float awarenessTimer;
        private Transform player;

        private bool wasVisible;

        private void Awake()
        {
            agentBehaviour = GetComponent<AgentBehaviour>();
            actionProvider = GetComponent<GoapActionProvider>();
            memory = GetComponent<EnemyMemory>();
        }

        private void Start()
        {
            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;

            actionProvider.RequestGoal<WanderGoal>();
        }

        private void Update()
        {
            if (actionProvider.AgentType == null || player == null)
                return;

            bool visible = IsPlayerVisible();

            //bool justSeen = visible && !wasVisible;
            bool justLost = !visible && wasVisible;

            switch (state)
            {
                // NORMAL (wandering until player is seen)
                case AIState.Normal:

                    if (visible)
                    {
                        actionProvider.RequestGoal<ChaseGoal>();
                    }

                    if (justLost)
                    {
                        EnterAware();
                    }

                    break;

                // AWARE (searching / suspicion timer)
                case AIState.Aware:

                    if (visible)
                    {
                        EnterNormal();
                        actionProvider.RequestGoal<ChaseGoal>();
                        break;
                    }

                    float bonus = 0f;

                    if (Vector3.Distance(transform.position, player.position) <= ProximityThreshold)
                        bonus = ProximityAwarenessBonus;

                    awarenessTimer -= Time.deltaTime;

                    if (awarenessTimer - bonus <= 0f)
                        EnterInvestigate();

                    break;

                // INVESTIGATE (go to last known position)
                case AIState.Investigate:

                    if (visible)
                    {
                        EnterNormal();
                        actionProvider.RequestGoal<ChaseGoal>();
                        break;
                    }

                    if (!memory.HasLastKnownPosition)
                    {
                        EnterNormal();
                        actionProvider.RequestGoal<WanderGoal>();
                    }

                    break;
            }

            wasVisible = visible;
        }

        // STATE TRANSITIONS
        private void EnterNormal()
        {
            state = AIState.Normal;
            awarenessTimer = 0f;
        }

        private void EnterAware()
        {
            state = AIState.Aware;
            awarenessTimer = BaseAwarenessDuration;

            if (player != null)
            {
                var pos = player.position;

                // Sample nearest NavMesh point within 5 units vertically
                // This handles multi-floor levels correctly
                if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas))
                {
                    pos = hit.position;
                }

                else
                {
                    pos.y = transform.position.y; // fallback if no NavMesh found
                }
                    

                memory.LastKnownPlayerPosition = pos;
                memory.HasLastKnownPosition = true;
            }
            else
            {
                Debug.Log("[AgentBrain] EnterAware — player ref is null!");
            }
            
            // Immediately start moving to last known position
            actionProvider.RequestGoal<InvestigateGoal>();
        }

        private void EnterInvestigate()
        {
            state = AIState.Investigate;
            Debug.Log($"[AgentBrain] EnterInvestigate — hasPos={memory.HasLastKnownPosition} pos={memory.LastKnownPlayerPosition}");
            actionProvider.RequestGoal<InvestigateGoal>();
        }

        // VISIBILITY CHECK
        private bool IsPlayerVisible()
        {
            if (player == null) return false;

            Vector3 origin = transform.position + Vector3.up * 1.5f;
            Vector3 toPlayer = player.position - origin;

            float distance = toPlayer.magnitude;

            if (distance > DetectionRange)
                return false;

            if (Vector3.Angle(transform.forward, toPlayer) > FieldOfViewAngle * 0.5f)
            {
                //Debug.Log($"[FOV] angle={Vector3.Angle(transform.forward, toPlayer):F1} limit={FieldOfViewAngle * 0.5f}");
                return false;
            }

            int mask = LayerMask.GetMask("Default", "Player");

            if (Physics.Raycast(origin, toPlayer.normalized, out RaycastHit hit, distance, mask))
                if (!hit.transform.CompareTag("Player"))
                    return false;

            return true;
        }
    }
}