using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Goals;
using UnityEngine;
using UnityEngine.AI;
using Combat.Stats;
using Combat.Spawning;
using Combat.Enemies;

namespace GOAPGettingStarted.Behaviours
{
    // Owns perception and picks the goal. One sight check feeds both the state machine and
    // GOAP (PlayerVisibleSensor reads IsTracking), so they can never disagree.
    //
    // Wander -> Engaged: seen in the view cone, sensed close by (any facing), shot, or gunfire heard.
    // Engaged: line of sight alone keeps contact. Relentless enemies never give up while the player
    //          is alive; otherwise the player is lost after LoseSightGrace out of sight.
    // Engaged -> Search: walk to where the player was last seen, look toward their heading.
    // Search -> Wander: the search finishes (InvestigateAction clears the memory).
    [RequireComponent(typeof(AgentBehaviour))]
    [RequireComponent(typeof(GoapActionProvider))]
    [RequireComponent(typeof(EnemyMemory))]
    public class AgentBrain : MonoBehaviour, IPoolable
    {
        public enum AIState { Wander, Engaged, Search }

        [Header("Detection")]
        public float DetectionRange = 25f;
        public float FieldOfViewAngle = 120f;

        [Header("Awareness")]
        [Tooltip("Within this distance the player is noticed whatever way the enemy faces (line of sight still needed).")]
        public float SenseRadius = 10f;
        [Tooltip("Seconds out of sight before an engaged enemy gives up and searches (ignored when Relentless).")]
        public float LoseSightGrace = 5f;
        [Tooltip("Engaged enemies chase until the player is dead and never fall back to searching.")]
        public bool Relentless = true;
        [Tooltip("Player gunfire within this distance alerts the enemy (m).")]
        public float HearingRadius = 80f;
        [Tooltip("Seconds between sight checks.")]
        public float PerceptionInterval = 0.1f;
        [Tooltip("Height of the enemy's eyes above its root.")]
        public float EyeHeight = 1.5f;
        [Tooltip("Height on the player the sight ray aims at.")]
        public float PlayerAimHeight = 1.2f;

        [Header("Movement (tuning - pushed as stat bases at Start)")]
        [SerializeField] private float baseMoveSpeed = 3.5f;
        [SerializeField] private float chaseSpeedMultiplier = 1.6f;
        [SerializeField] private float investigateSpeedMultiplier = 1.3f;

        [Header("Movement Stats")]
        [Tooltip("This enemy's CombatantStats (auto-found on this object if empty).")]
        [SerializeField] private CombatantStats combatantStats;
        [Tooltip("References to the movement stat definitions.")]
        [SerializeField] private EnemyMovementStatKeys movementKeys;

        [Header("Debug")]
        [SerializeField] private bool logState = false;

        public float BaseMoveSpeed
            => Stat(movementKeys != null ? movementKeys.moveSpeed : null, baseMoveSpeed);
        public float ChaseSpeedMultiplier
            => Stat(movementKeys != null ? movementKeys.chaseSpeedMultiplier : null, chaseSpeedMultiplier);
        public float InvestigateSpeedMultiplier
            => Stat(movementKeys != null ? movementKeys.investigateSpeedMultiplier : null, investigateSpeedMultiplier);

        // Which speed multiplier the current action wants (1 = normal/wander)
        public float CurrentSpeedMultiplier { get; set; } = 1f;

        public AIState State { get; private set; } = AIState.Wander;

        // Engaged and in contact (or within the grace); GOAP's PlayerVisible
        public bool IsTracking => State == AIState.Engaged;

        private GoapActionProvider actionProvider;
        private EnemyMemory memory;
        private CombatantHealth health;
        private EnemyDefinitionApplier definition;

        private Transform player;
        private CombatantStats playerStats;
        private int sightMask;
        private float nextPerception;
        private float lastContactTime = float.NegativeInfinity;
        private Vector3 lastSeenPosition;
        private Vector3 lastSeenVelocity;
        private Vector3 previousPlayerPosition;
        private float previousSampleTime;
        private float lastHealth = -1f;

        private void Awake()
        {
            actionProvider = GetComponent<GoapActionProvider>();
            memory = GetComponent<EnemyMemory>();
            health = GetComponent<CombatantHealth>();
            definition = GetComponent<EnemyDefinitionApplier>();
            if (combatantStats == null) combatantStats = GetComponent<CombatantStats>();
            sightMask = LayerMask.GetMask("Default", "Ground", "Player");
        }

        private void OnEnable()
        {
            if (health != null) health.HealthChanged += HandleHealthChanged;
            AINoise.Heard += HandleNoise;
        }

        private void OnDisable()
        {
            if (health != null) health.HealthChanged -= HandleHealthChanged;
            AINoise.Heard -= HandleNoise;
        }

        private void Start()
        {
            PushMovementBases();
            FindPlayer();
            EnterWander();
        }

        // ---- IPoolable ----

        // Reused enemy: wipe awareness and re-plan from scratch (Start doesn't run on reuse)
        public void OnSpawn()
        {
            CurrentSpeedMultiplier = 1f;
            lastContactTime = float.NegativeInfinity;
            lastHealth = -1f;
            FindPlayer();
            if (actionProvider != null) EnterWander();
        }

        public void OnDespawn() { }

        private void FindPlayer()
        {
            var playerObj = GameObject.FindWithTag("Player");
            player = playerObj != null ? playerObj.transform : null;
            playerStats = player != null ? player.GetComponent<CombatantStats>() : null;
            if (player != null)
            {
                previousPlayerPosition = player.position;
                previousSampleTime = Time.time;
            }
        }

        // ===== Frame =====

        private void Update()
        {
            if (actionProvider.AgentType == null || player == null) return;

            // Nothing left to chase
            if (playerStats != null && playerStats.IsDying)
            {
                if (State != AIState.Wander) EnterWander();
                return;
            }

            if (Time.time >= nextPerception)
            {
                nextPerception = Time.time + PerceptionInterval;
                Perceive();
            }

            switch (State)
            {
                case AIState.Engaged:
                    if (!Relentless && Time.time - lastContactTime > LoseSightGrace) EnterSearch();
                    break;

                case AIState.Search:
                    if (!memory.HasLastKnownPosition) EnterWander();
                    break;
            }
        }

        // One sight check; contact refreshes the last-seen data, and noticing engages
        private void Perceive()
        {
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            Vector3 target = player.position + Vector3.up * PlayerAimHeight;
            Vector3 to = target - eye;
            float distance = to.magnitude;

            SamplePlayerVelocity();

            if (distance > DetectionRange || !HasLineOfSight(eye, to, distance)) return;

            Vector3 flatTo = to; flatTo.y = 0f;
            Vector3 flatFwd = transform.forward; flatFwd.y = 0f;
            bool inCone = Vector3.Angle(flatFwd, flatTo) <= FieldOfViewAngle * 0.5f;
            bool sensed = distance <= SenseRadius;

            // Once engaged, line of sight alone keeps contact
            if (!inCone && !sensed && State != AIState.Engaged) return;

            MarkContact();
            if (State != AIState.Engaged) EnterEngaged();
        }

        // Anything on the player (controller, body, hitboxes) counts as the player; triggers ignored
        private bool HasLineOfSight(Vector3 eye, Vector3 to, float distance)
        {
            if (!Physics.Raycast(eye, to / distance, out var hit, distance, sightMask, QueryTriggerInteraction.Ignore))
                return true;
            return hit.transform == player || hit.transform.IsChildOf(player);
        }

        private void SamplePlayerVelocity()
        {
            float dt = Time.time - previousSampleTime;
            if (dt <= 0f) return;
            Vector3 v = (player.position - previousPlayerPosition) / dt;
            v.y = 0f;
            if (v.sqrMagnitude > 0.04f) lastSeenVelocity = v;
            previousPlayerPosition = player.position;
            previousSampleTime = Time.time;
        }

        private void MarkContact()
        {
            lastContactTime = Time.time;
            lastSeenPosition = player.position;
        }

        // Any drop in health means the player hit us: engage and turn on them, even from behind
        private void HandleHealthChanged(float current, float max)
        {
            bool damaged = lastHealth >= 0f && current < lastHealth - 0.001f && current > 0f;
            lastHealth = current;
            if (!damaged || player == null) return;

            MarkContact();
            if (State != AIState.Engaged) EnterEngaged();
        }

        // Gunfire within hearing range: engage and turn toward it
        private void HandleNoise(Vector3 position, Transform source)
        {
            if (player == null || (playerStats != null && playerStats.IsDying)) return;
            Vector3 d = position - transform.position;
            if (d.sqrMagnitude > HearingRadius * HearingRadius) return;

            lastContactTime = Time.time;
            lastSeenPosition = position;
            if (State != AIState.Engaged) EnterEngaged();
        }

        // ===== States =====

        private void EnterWander()
        {
            SetState(AIState.Wander);
            memory.HasLastKnownPosition = false;
            actionProvider.RequestGoal<WanderGoal>();
        }

        private void EnterEngaged()
        {
            SetState(AIState.Engaged);
            actionProvider.RequestGoal<ChaseGoal>();
        }

        private void EnterSearch()
        {
            SetState(AIState.Search);

            Vector3 pos = lastSeenPosition;
            if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas)) pos = hit.position;
            else pos.y = transform.position.y;

            memory.LastKnownPlayerPosition = pos;
            memory.LastKnownHeading = lastSeenVelocity.sqrMagnitude > 0.01f
                ? lastSeenVelocity.normalized
                : Flat(pos - transform.position).normalized;
            memory.HasLastKnownPosition = true;

            actionProvider.RequestGoal<InvestigateGoal>();
        }

        private void SetState(AIState next)
        {
            if (logState && next != State) Debug.Log($"[AgentBrain] {name}: {State} -> {next}", this);
            State = next;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ===== Stats =====

        private float Stat(StatDefinitionSO def, float fallback)
        {
            var c = combatantStats != null ? combatantStats.Container : null;
            if (c == null || def == null) return fallback;
            return c.Resolve(def);
        }

        // Serialized tuning -> stat bases, skipping any the enemy definition owns. Modifiers layer on top.
        private void PushMovementBases()
        {
            var c = combatantStats != null ? combatantStats.Container : null;
            if (c == null || movementKeys == null) return;

            PushBase(c, movementKeys.moveSpeed, baseMoveSpeed);
            PushBase(c, movementKeys.chaseSpeedMultiplier, chaseSpeedMultiplier);
            PushBase(c, movementKeys.investigateSpeedMultiplier, investigateSpeedMultiplier);
        }

        private void PushBase(StatContainer c, StatDefinitionSO def, float value)
        {
            if (def == null) return;
            if (definition != null && definition.DefinesBase(def)) return;
            c.SetBase(def, value);
        }
    }
}
