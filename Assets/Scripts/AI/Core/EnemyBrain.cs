using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using Combat.AI.Goap;
using Combat.Spawning;

namespace Combat.AI
{
    // Binds the GOAP agent type and asks for every goal at once; the planner picks the cheapest one it can reach.
    // Re-plans the moment perception flips (target gained or lost, search done) instead of waiting for an action.
    // Runs before the provider's Awake so the agent type is set before anything reads it.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(AgentBehaviour))]
    [RequireComponent(typeof(GoapActionProvider))]
    [RequireComponent(typeof(EnemyPerception))]
    [RequireComponent(typeof(EnemyMover))]
    public class EnemyBrain : MonoBehaviour, IPoolable
    {
        [Tooltip("Agent type built in code (see EnemyAgentTypes).")]
        [SerializeField] private string agentType = EnemyAgentTypes.Melee;

        [Header("Debug")]
        [SerializeField] private bool logState = false;

        private GoapActionProvider provider;
        private EnemyPerception perception;
        private bool bound;

        // Set when a wander starts so the wander target sensor rolls a fresh point instead of resuming the old one.
        public bool WanderReroll { get; set; } = true;

        private void Awake()
        {
            provider = GetComponent<GoapActionProvider>();
            perception = GetComponent<EnemyPerception>();

            var type = EnemyAgentTypes.Get(agentType);
            if (type == null) return;
            provider.AgentType = type;
            bound = true;
        }

        private void OnEnable()
        {
            perception.Changed += HandlePerceptionChanged;
        }

        private void OnDisable()
        {
            perception.Changed -= HandlePerceptionChanged;
        }

        private void Start() => RequestGoals();

        // ---- IPoolable ----

        public void OnSpawn()
        {
            WanderReroll = true;
            RequestGoals();
        }

        public void OnDespawn() { }

        private void RequestGoals()
        {
            if (!bound) return;
            provider.RequestGoal<KillTargetGoal, SearchGoal, IdleGoal>(true);
        }

        private void HandlePerceptionChanged()
        {
            if (logState)
                Debug.Log($"[EnemyBrain] {name}: target={perception.HasTarget} lastKnown={perception.HasLastKnown}", this);
            if (bound && isActiveAndEnabled) provider.ResolveAction();
        }
    }
}
