using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using Combat.AI.Goap;
using Combat.Enemies;
using Combat.Spawning;

namespace Combat.AI
{
    // Binds the GOAP agent type for this enemy's behaviour profile and asks for every goal at once;
    // the planner picks the cheapest one it can reach. Re-plans the moment perception, the shield or
    // the token state flips. Re-binds when the enemy definition (and so its profile) changes.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(AgentBehaviour))]
    [RequireComponent(typeof(GoapActionProvider))]
    [RequireComponent(typeof(EnemyPerception))]
    [RequireComponent(typeof(EnemyMover))]
    public class EnemyBrain : MonoBehaviour, IPoolable
    {
        [Tooltip("Used when the enemy definition sets no profile. Empty = plain melee.")]
        [SerializeField] private EnemyBehaviourProfileSO defaultProfile;

        [Header("Debug")]
        [SerializeField] private bool logState = false;

        private const float CoverRecheck = 1f;

        private GoapActionProvider provider;
        private AgentBehaviour agent;
        private EnemyPerception perception;
        private EnemyDefinitionApplier definition;
        private CombatantHealth health;
        private EnemyShield shield;
        private bool bound;
        private bool started;

        private bool hasCover;
        private Vector3 coverPoint;
        private float nextCoverCheck;

        public EnemyBehaviourProfileSO Profile { get; private set; }

        // Set when a wander starts so the wander target sensor rolls a fresh point instead of resuming the old one.
        public bool WanderReroll { get; set; } = true;

        public bool HasToken => AttackTokens.Holds(perception.Target, this);

        // Shield broke and hasn't fully recharged yet.
        public bool ShieldDown { get; private set; }

        public bool WantsCover => ShieldDown && Profile != null && Profile.shieldCover;

        private void Awake()
        {
            provider = GetComponent<GoapActionProvider>();
            agent = GetComponent<AgentBehaviour>();
            perception = GetComponent<EnemyPerception>();
            definition = GetComponent<EnemyDefinitionApplier>();
            health = GetComponent<CombatantHealth>();
            // The provider needs its agent type before its own Start
            Bind(ResolveProfile());
        }

        private void OnEnable()
        {
            perception.Changed += HandlePerceptionChanged;
            if (definition != null) definition.Applied += HandleDefinitionApplied;
            if (health != null) health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            perception.Changed -= HandlePerceptionChanged;
            if (definition != null) definition.Applied -= HandleDefinitionApplied;
            if (health != null) health.OnDeath -= HandleDeath;
            ReleaseToken();
            CoverFinder.Release(this);
        }

        private void Start()
        {
            started = true;
            RequestGoals();
        }

        // ---- IPoolable ----

        public void OnSpawn()
        {
            WanderReroll = true;
            ShieldDown = false;
            hasCover = false;
            ReleaseToken();
            Bind(ResolveProfile());
            RequestGoals();
        }

        public void OnDespawn() { }

        // ---- binding ----

        private EnemyBehaviourProfileSO ResolveProfile()
        {
            var p = definition != null ? definition.BehaviourProfile : null;
            return p != null ? p : defaultProfile;
        }

        private void HandleDefinitionApplied() => Bind(ResolveProfile());

        private void Bind(EnemyBehaviourProfileSO profile)
        {
            var type = EnemyAgentTypes.Get(profile);
            if (type == null) return;
            Profile = profile;
            if (bound && provider.AgentType == type) return;

            if (bound && agent.ActionState.Action != null) agent.StopAction(false);
            provider.AgentType = type;
            bound = true;
            RequestGoals();
        }

        private void RequestGoals()
        {
            if (!bound || !started) return;
            provider.RequestGoal<KillTargetGoal, StayAliveGoal, SearchGoal, IdleGoal>(true);
        }

        private void Replan()
        {
            if (bound && started && isActiveAndEnabled) provider.ResolveAction();
        }

        // ---- state ----

        private void Update()
        {
            UpdateShield();
        }

        private void UpdateShield()
        {
            if (shield == null) shield = GetComponent<EnemyShield>();
            bool down = ShieldDown;
            if (shield == null || shield.MaxShield <= 0f) down = false;
            else if (shield.CurrentShield <= 0f) down = true;
            else if (shield.CurrentShield >= shield.MaxShield - 0.01f) down = false;

            if (down == ShieldDown) return;
            ShieldDown = down;
            if (!down) { hasCover = false; CoverFinder.Release(this); }
            if (logState) Debug.Log($"[EnemyBrain] {name}: shieldDown={down}", this);
            Replan();
        }

        private void HandlePerceptionChanged()
        {
            if (!perception.HasTarget) ReleaseToken();
            if (logState)
                Debug.Log($"[EnemyBrain] {name}: target={perception.HasTarget} lastKnown={perception.HasLastKnown}", this);
            Replan();
        }

        private void HandleDeath() => ReleaseToken();

        public void ReleaseToken() => AttackTokens.ReleaseAll(this);

        // Cover from the current target, kept until the target can see it again.
        public bool TryGetCover(out Vector3 point)
        {
            point = coverPoint;
            if (!WantsCover || perception.Target == null) return false;

            Vector3 threat = perception.Target.position;
            bool recheck = !hasCover || (Time.time >= nextCoverCheck && !CoverFinder.Hidden(coverPoint, CoverFinder.EyeOf(threat)));
            if (recheck)
            {
                nextCoverCheck = Time.time + CoverRecheck;
                var p = Profile;
                if (!CoverFinder.Find(transform.position, threat, p.coverSearchRadius, p.coverMinTargetDistance, this, out coverPoint))
                    coverPoint = CoverFinder.AwayFrom(transform.position, threat, p.retreatDistance);
                hasCover = true;
            }
            point = coverPoint;
            return true;
        }
    }
}
