using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Goals;
using UnityEngine;
using UnityEngine.AI;
using Combat.Stats;

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

        private AIState lastLoggedState;



        // MOVEMENT — tuning values, pushed into the stat container as BASES at Start.
        // The public properties below resolve from the container, so slows/speed buffs
        // are modifiers. Callers (ChaseAction, InvestigateAction, AgentMoveBehaviour)
        // read the properties and need no changes.
        [Header("Movement (tuning — pushed as stat bases at Start)")]
        [SerializeField] private float baseMoveSpeed = 3.5f;
        [SerializeField] private float chaseSpeedMultiplier = 1.6f;
        [SerializeField] private float investigateSpeedMultiplier = 1.3f;

        [Header("Movement Stats")]
        [Tooltip("This enemy's CombatantStats (auto-found on this object if empty).")]
        [SerializeField] private CombatantStats combatantStats;
        [Tooltip("References to the movement stat definitions.")]
        [SerializeField] private EnemyMovementStatKeys movementKeys;

        // Resolved movement stats (live, cached — a slow applies on the next read).
        // Serialized tuning value is the fallback if stats aren't wired.
        public float BaseMoveSpeed
            => Stat(movementKeys != null ? movementKeys.moveSpeed : null, baseMoveSpeed);
        public float ChaseSpeedMultiplier
            => Stat(movementKeys != null ? movementKeys.chaseSpeedMultiplier : null, chaseSpeedMultiplier);
        public float InvestigateSpeedMultiplier
            => Stat(movementKeys != null ? movementKeys.investigateSpeedMultiplier : null, investigateSpeedMultiplier);

        // Which speed multiplier the current action wants (1 = normal/wander).
        // Actions set this instead of writing nav.speed directly, so AgentMoveBehaviour
        // can re-apply speed every frame and slows land instantly mid-state.
        public float CurrentSpeedMultiplier { get; set; } = 1f;

        private float Stat(StatDefinitionSO def, float fallback)
        {
            var c = combatantStats != null ? combatantStats.Container : null;
            if (c == null || def == null) return fallback;
            return c.Resolve(def);
        }

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

            if (combatantStats == null)
                combatantStats = GetComponent<CombatantStats>();
        }

        private void Start()
        {
            PushMovementBases();

            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;

            actionProvider.RequestGoal<WanderGoal>();
        }

        // Serialized tuning -> stat bases. Modifiers layer on top of these.
        private void PushMovementBases()
        {
            var c = combatantStats != null ? combatantStats.Container : null;
            if (c == null || movementKeys == null) return;

            if (movementKeys.moveSpeed != null)
                c.SetBase(movementKeys.moveSpeed, baseMoveSpeed);
            if (movementKeys.chaseSpeedMultiplier != null)
                c.SetBase(movementKeys.chaseSpeedMultiplier, chaseSpeedMultiplier);
            if (movementKeys.investigateSpeedMultiplier != null)
                c.SetBase(movementKeys.investigateSpeedMultiplier, investigateSpeedMultiplier);
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

            //if (visible != wasVisible)
                //Debug.Log($"[Brain] {state} | visible {wasVisible}->{visible} | hasPos={memory.HasLastKnownPosition}");

            /*if (state != lastLoggedState)
            {
                Debug.Log($"[Brain] STATE -> {state} | visible={visible} | hasPos={memory.HasLastKnownPosition}");
                lastLoggedState = state;
            }*/

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
            //Debug.Log($"[AgentBrain] EnterInvestigate — hasPos={memory.HasLastKnownPosition} pos={memory.LastKnownPlayerPosition}");
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
            {
                //if (Time.frameCount % 30 == 0) Debug.Log($"[Vis] FAIL range {distance:F1} > {DetectionRange}");
                return false;
            }

            Vector3 flatToPlayer = toPlayer; flatToPlayer.y = 0f;
            Vector3 flatForward = transform.forward; flatForward.y = 0f;
            if (Vector3.Angle(flatForward, flatToPlayer) > FieldOfViewAngle * 0.5f)
                return false;

            int mask = LayerMask.GetMask("Default", "Player");
            if (Physics.Raycast(origin, toPlayer.normalized, out RaycastHit hit, distance, mask))
            {
                if (!hit.transform.CompareTag("Player"))
                {
                    //if (Time.frameCount % 30 == 0) Debug.Log($"[Vis] FAIL blocked by {hit.transform.name} (tag {hit.transform.tag})");
                    return false;
                }
            }

            return true;
        }
    }
}