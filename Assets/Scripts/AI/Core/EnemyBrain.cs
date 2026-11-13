using System;
using System.Collections.Generic;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;
using Combat.AI.Goap;
using Combat.Enemies;
using Combat.Spawning;

namespace Combat.AI
{
    public enum DefensiveBand { Aggressive, Normal, Defensive }

    // Decisions an enemy announces (barks, debug, later squads listen to these).
    public enum EnemyDecision { Spotted, LostTarget, Engaging, Repositioning, Advancing, Reloading, Flinched, Outranged, TakingCover, Peeking, Suppressing }

    // Binds the GOAP agent type for this enemy's behaviour profile and asks for every goal at once;
    // the planner picks the cheapest one it can reach. Re-plans the moment perception, the shield,
    // the range band or the defensiveness band flips. Owns tokens, defensiveness and the firing point.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(AgentBehaviour))]
    [RequireComponent(typeof(GoapActionProvider))]
    [RequireComponent(typeof(EnemyPerception))]
    [RequireComponent(typeof(EnemyMover))]
    public class EnemyBrain : MonoBehaviour, IPoolable
    {
        public struct DecisionRecord
        {
            public EnemyDecision Decision;
            public float Time;
        }

        [Tooltip("Used when the enemy definition sets no profile. Empty = plain melee.")]
        [SerializeField] private EnemyBehaviourProfileSO defaultProfile;

        [Header("Debug")]
        [SerializeField] private bool logState = false;

        private const float CoverRecheck = 1f;
        private const int RecentSize = 4;

        // Every enabled brain, for spacing and the debug overlay.
        public static readonly List<EnemyBrain> Active = new List<EnemyBrain>();

        // Any brain's decision, for listeners that don't hold a reference (overlay, barks).
        public static event Action<EnemyBrain, EnemyDecision> AnyDecided;
        private static event Action<EnemyBrain> AnyDied;

        // The debug overlay turns this on to keep scored candidates.
        public static bool RecordPoints;

        private GoapActionProvider provider;
        private AgentBehaviour agent;
        private EnemyPerception perception;
        private EnemyDefinitionApplier definition;
        private CombatantHealth health;
        private EnemyShield shield;
        private EnemyRangedAttack ranged;
        private EnemyMeleeAttack melee;
        private bool bound;
        private bool started;

        private bool hasCover;
        private Vector3 coverPoint;
        private float nextCoverCheck;

        private bool hadTarget;
        private float lastHealth = -1f;
        private bool insideMinRange;
        private DefensiveBand bandAtEval;

        private bool hasFiringPoint;
        private bool firingPointValid;
        private Vector3 firingPoint;
        private float nextFiringCheck;
        private float holdUntil = -1f;   // < 0: not yet settled at the current point
        private const float BandSlack = 0.05f;

        // Combat cover (ranged): where to hide and where to peek from
        public const float CoverTolerance = 0.6f;
        public const float CoverArea = 2.5f;
        private const float NoCoverRetry = 2f;
        private bool hasCombatCover;
        private CoverSpot combatCover;
        private float nextCombatCoverCheck;
        private float coverHoldUntil = -1f;   // < 0: not yet settled at the current cover

        private readonly DecisionRecord[] recent = new DecisionRecord[RecentSize];
        private int recentCount;
        private int recentHead;

        public event Action<EnemyDecision> Decided;

        public EnemyBehaviourProfileSO Profile { get; private set; }
        public GoapActionProvider Provider => provider;
        public AgentBehaviour Agent => agent;
        public EnemyPerception Perception => perception;
        public EnemyRangedAttack Ranged => ranged;

        // Set when a wander starts so the wander target sensor rolls a fresh point instead of resuming the old one.
        public bool WanderReroll { get; set; } = true;

        public bool HasToken => AttackTokens.Holds(perception.Target, this);
        public bool HasRangedToken => AttackTokens.Holds(perception.Target, this, AttackKind.Ranged);

        // Shield broke and hasn't fully recharged yet.
        public bool ShieldDown { get; private set; }

        public bool WantsCover => ShieldDown && Profile != null && Profile.shieldCover;

        // 0 calm .. 1 threatened, and its band
        public float Defensiveness { get; private set; }
        public DefensiveBand Band { get; private set; } = DefensiveBand.Normal;

        // Optional volume firing points must stay inside (set by encounter logic later).
        public Collider FiringArea { get; set; }

        public bool IsRangedProfile => Profile != null && Profile.ranged && ranged != null;
        public bool InsideMinRange => insideMinRange;

        // Melee only when the profile has no gun or the target is inside the min range
        public bool MeleeAllowed => !IsRangedProfile || insideMinRange;
        public bool RangedAllowed => IsRangedProfile && !(insideMinRange && Profile.melee && melee != null);

        public bool NeedsReload => ranged != null && ranged.NeedsReload;
        public bool HasFiringPoint => hasFiringPoint;
        public bool FiringPointValid => firingPointValid;
        public Vector3 FiringPoint => firingPoint;
        public bool LastPointSearchFailed { get; private set; }
        public List<ScoredPoint> DebugCandidates { get; private set; }

        public bool UsesCover => IsRangedProfile && Profile.useCover;
        public bool HasCombatCover => hasCombatCover;
        public CoverSpot CombatCover => combatCover;

        // Within reach of the cover spot (hiding there, at its peek spot or between them)
        public bool InCover => hasCombatCover && Flat(transform.position - combatCover.Point).magnitude <= CoverArea;

        public bool InFiringPosition
            => hasFiringPoint && firingPointValid && Profile != null
               && Flat(transform.position - firingPoint).magnitude <= Profile.firingPointTolerance;

        // At the firing point or strafing beside it
        public bool InFiringArea
            => hasFiringPoint && firingPointValid && Profile != null
               && Flat(transform.position - firingPoint).magnitude <= Profile.firingPointTolerance + (Profile.strafe ? Profile.strafeDistance : 0f);

        public float TargetDistance => perception.HasTarget || perception.HasLastKnown
            ? Flat(perception.BelievedPosition - transform.position).magnitude : float.MaxValue;

        public int RecentDecisionCount => recentCount;

        // Newest first
        public DecisionRecord RecentDecision(int i) => recent[(recentHead - 1 - i + RecentSize * 2) % RecentSize];

        private void Awake()
        {
            provider = GetComponent<GoapActionProvider>();
            agent = GetComponent<AgentBehaviour>();
            perception = GetComponent<EnemyPerception>();
            definition = GetComponent<EnemyDefinitionApplier>();
            health = GetComponent<CombatantHealth>();
            ranged = GetComponent<EnemyRangedAttack>();
            melee = GetComponent<EnemyMeleeAttack>();
            // The provider needs its agent type before its own Start
            Bind(ResolveProfile());
        }

        private void OnEnable()
        {
            Active.Add(this);
            perception.Changed += HandlePerceptionChanged;
            AnyDied += HandleAllyDied;
            if (definition != null) definition.Applied += HandleDefinitionApplied;
            if (health != null) { health.OnDeath += HandleDeath; health.HealthChanged += HandleHealthChanged; }
            if (ranged != null) ranged.Flinched += HandleFlinched;
        }

        private void OnDisable()
        {
            int i = Active.IndexOf(this);
            if (i >= 0) { Active[i] = Active[Active.Count - 1]; Active.RemoveAt(Active.Count - 1); }
            perception.Changed -= HandlePerceptionChanged;
            AnyDied -= HandleAllyDied;
            if (definition != null) definition.Applied -= HandleDefinitionApplied;
            if (health != null) { health.OnDeath -= HandleDeath; health.HealthChanged -= HandleHealthChanged; }
            if (ranged != null) ranged.Flinched -= HandleFlinched;
            ReleaseToken();
            CoverFinder.Release(this);
            PositionScorer.Release(this);
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
            hadTarget = false;
            lastHealth = -1f;
            insideMinRange = false;
            Defensiveness = 0f;
            Band = DefensiveBand.Normal;
            ClearFiringPoint();
            ClearCombatCover();
            recentCount = 0;
            recentHead = 0;
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
            if (ranged != null) ranged.ApplyProfile(profile);
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

        // ---- decisions ----

        public void Raise(EnemyDecision d)
        {
            recent[recentHead] = new DecisionRecord { Decision = d, Time = Time.time };
            recentHead = (recentHead + 1) % RecentSize;
            if (recentCount < RecentSize) recentCount++;
            Decided?.Invoke(d);
            AnyDecided?.Invoke(this, d);
            if (logState) Debug.Log($"[EnemyBrain] {name}: {d}", this);
        }

        // ---- state ----

        private void Update()
        {
            UpdateShield();
            if (Profile == null) return;
            UpdateDefensiveness(Time.deltaTime);
            if (IsRangedProfile) UpdateRanged();
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

        // Rises with damage, being aimed at, allies dying and a close target; decays back to calm
        private void UpdateDefensiveness(float dt)
        {
            var p = Profile;
            float d = Defensiveness;
            if (perception.HasTarget)
            {
                if (perception.AimedAt) d += p.defAimedAt * dt;
                if (TargetDistance < p.rangedMinRange) d += p.defProximity * dt;
            }
            d -= p.defDecay * dt;
            Defensiveness = Mathf.Clamp01(d);

            // Slack around each threshold so hovering near one doesn't flip the band back and forth
            float aggressiveAt = Band == DefensiveBand.Aggressive ? p.aggressiveBelow + BandSlack : p.aggressiveBelow - BandSlack;
            float defensiveAt = Band == DefensiveBand.Defensive ? p.defensiveAbove - BandSlack : p.defensiveAbove + BandSlack;
            var band = Defensiveness < aggressiveAt ? DefensiveBand.Aggressive
                     : Defensiveness > defensiveAt ? DefensiveBand.Defensive : DefensiveBand.Normal;
            if (band == Band) return;
            Band = band;
            // Shooters re-pick their firing point when the hold allows; no plan reset mid-burst
            if (!IsRangedProfile) Replan();
        }

        private void AddDefensiveness(float amount)
        {
            Defensiveness = Mathf.Clamp01(Defensiveness + amount);
        }

        private void UpdateRanged()
        {
            if (!perception.HasTarget)
            {
                if (hasFiringPoint) ClearFiringPoint();
                if (hasCombatCover) ClearCombatCover();
                return;
            }

            bool inside = TargetDistance < Profile.rangedMinRange;
            if (inside != insideMinRange)
            {
                insideMinRange = inside;
                Replan();
            }

            // Fighting from cover: no firing point needed
            if (UsesCover && UpdateCombatCover()) return;

            // Reached an advance point: look again straight away
            if (hasFiringPoint && !firingPointValid && Flat(transform.position - firingPoint).magnitude <= Profile.firingPointTolerance)
                nextFiringCheck = 0f;

            // Settled at the point: hold it a while before looking for a better one
            float now = Time.time;
            if (holdUntil < 0f && InFiringPosition)
                holdUntil = now + UnityEngine.Random.Range(Profile.pointHoldMin, Mathf.Max(Profile.pointHoldMin, Profile.pointHoldMax));

            if (now < nextFiringCheck) return;

            bool valid = hasFiringPoint && firingPointValid && ValidPoint(firingPoint);
            bool holding = holdUntil >= 0f && now < holdUntil;
            bool reconsider = holdUntil >= 0f && now >= holdUntil;
            if (valid && (holding || (bandAtEval == Band && !reconsider)))
            {
                nextFiringCheck = now + Profile.firingPointRecheck;
                return;
            }
            if (!PositionScorer.TryBeginEvaluation()) return;   // over this frame's budget: try next frame
            nextFiringCheck = now + Profile.firingPointRecheck;
            holdUntil = -1f;
            EvaluateFiringPoint();
        }

        // Keeps the cover while it hides and its hold lasts; true while there is cover to fight from
        private bool UpdateCombatCover()
        {
            float now = Time.time;
            if (coverHoldUntil < 0f && hasCombatCover && Flat(transform.position - combatCover.Point).magnitude <= CoverTolerance)
                coverHoldUntil = now + UnityEngine.Random.Range(Profile.coverHoldMin, Mathf.Max(Profile.coverHoldMin, Profile.coverHoldMax));

            if (now < nextCombatCoverCheck) return hasCombatCover;

            bool valid = hasCombatCover
                         && CoverFinder.StillCovers(combatCover, perception.BelievedPosition, Profile.rangedMinRange, ranged.MaxRange);
            bool holding = coverHoldUntil < 0f || now < coverHoldUntil;
            if (valid && holding)
            {
                nextCombatCoverCheck = now + CoverRecheck;
                return true;
            }
            if (!PositionScorer.TryBeginEvaluation()) return hasCombatCover;   // over budget: keep what we have, retry next frame
            EvaluateCombatCover(now);
            return hasCombatCover;
        }

        private void EvaluateCombatCover(float now)
        {
            var p = Profile;
            var cam = Camera.main;
            Vector3 threat = perception.BelievedPosition;
            float min = p.rangedMinRange;
            float max = Mathf.Max(min + 1f, ranged.MaxRange);

            var q = new CoverQuery
            {
                Owner = this,
                Self = transform,
                From = transform.position,
                Threat = threat,
                ThreatViewForward = cam != null ? cam.transform.forward : transform.position - threat,
                MinRange = min,
                MaxRange = max,
                PreferredRange = PreferredRange(min, max),
                SearchRadius = p.firingPointSearchRadius,
                AllySpacing = p.allySpacing,
                Weights = p.coverWeights,
                HasCurrent = hasCombatCover,
                Current = combatCover.Point
            };

            bool had = hasCombatCover;
            if (!CoverFinder.FindCombat(q, out var spot))
            {
                ClearCombatCover();
                nextCombatCoverCheck = now + NoCoverRetry;
                if (had) Replan();
                return;
            }

            bool moved = !had || Flat(spot.Point - combatCover.Point).magnitude > CoverTolerance;
            combatCover = spot;
            hasCombatCover = true;
            coverHoldUntil = -1f;
            nextCombatCoverCheck = now + CoverRecheck;
            if (!moved) return;

            if (ranged != null) ranged.ResetAim();
            Raise(EnemyDecision.TakingCover);
            if (hasFiringPoint) ClearFiringPoint();
            // Switch plans now unless a burst is going; the shoot action resolves after it
            if (!had && (ranged == null || ranged.CurrentPhase != EnemyRangedAttack.Phase.Bursting)) Replan();
        }

        private void ClearCombatCover()
        {
            hasCombatCover = false;
            coverHoldUntil = -1f;
            nextCombatCoverCheck = 0f;
            CoverFinder.Release(this);
        }

        // The cover spot for the CombatCoverTarget sensor
        public bool TryGetCombatCover(out Vector3 point)
        {
            point = combatCover.Point;
            return hasCombatCover && perception.HasTarget;
        }

        private bool ValidPoint(Vector3 p)
        {
            Vector3 threat = perception.BelievedPosition;
            float d = Flat(p - threat).magnitude;
            if (d < Profile.rangedMinRange || d > ranged.MaxRange) return false;
            return PositionScorer.HasLineOfSight(p, threat);
        }

        private void EvaluateFiringPoint()
        {
            var p = Profile;
            Vector3 threat = perception.BelievedPosition;
            var cam = Camera.main;
            float min = p.rangedMinRange;
            float max = Mathf.Max(min + 1f, ranged.MaxRange);

            var q = new PointQuery
            {
                Purpose = PointPurpose.Fire,
                Owner = this,
                Self = transform,
                From = transform.position,
                Threat = threat,
                ThreatViewForward = cam != null ? cam.transform.forward : transform.position - threat,
                MinRange = min,
                MaxRange = max,
                PreferredRange = PreferredRange(min, max),
                SearchRadius = p.firingPointSearchRadius,
                AllySpacing = p.allySpacing,
                Weights = p.fireWeights,
                HasCurrent = hasFiringPoint && firingPointValid,
                Current = firingPoint,
                Area = FiringArea
            };

            if (RecordPoints && DebugCandidates == null) DebugCandidates = new List<ScoredPoint>(64);
            bandAtEval = Band;

            if (PositionScorer.Find(q, out var point, RecordPoints ? DebugCandidates : null))
            {
                LastPointSearchFailed = false;
                SetFiringPoint(point, true);
                return;
            }

            // Nowhere has sight of the target: advance along the line toward it and look again from there
            LastPointSearchFailed = true;
            Vector3 dir = Flat(threat - transform.position);
            float step = Mathf.Min(p.firingPointSearchRadius * 0.5f, Mathf.Max(1f, dir.magnitude - min));
            Vector3 ahead = transform.position + (dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward) * step;
            if (NavMesh.SamplePosition(ahead, out var hit, 3f, NavMesh.AllAreas)) SetFiringPoint(hit.position, false);
            else ClearFiringPoint();
        }

        // Aggressive leans toward the min range, Defensive toward the max
        private float PreferredRange(float min, float max)
        {
            float shift = Profile.rangedBandShift * (max - min);
            float pref = Profile.rangedPreferredRange;
            if (Band == DefensiveBand.Aggressive) pref -= shift;
            else if (Band == DefensiveBand.Defensive) pref += shift;
            return Mathf.Clamp(pref, min, max);
        }

        private void SetFiringPoint(Vector3 point, bool valid)
        {
            bool moved = !hasFiringPoint || Flat(point - firingPoint).magnitude > Profile.firingPointTolerance;
            bool hadPoint = hasFiringPoint;
            firingPoint = point;
            hasFiringPoint = true;
            firingPointValid = valid;
            if (!moved) return;
            if (ranged != null) ranged.ResetAim();
            if (!valid) Raise(EnemyDecision.Advancing);
            else if (hadPoint) Raise(EnemyDecision.Repositioning);
        }

        private void ClearFiringPoint()
        {
            hasFiringPoint = false;
            firingPointValid = false;
            LastPointSearchFailed = false;
            nextFiringCheck = 0f;
            holdUntil = -1f;
            PositionScorer.Release(this);
        }

        // The firing point for the FiringTarget sensor (valid or an advance point)
        public bool TryGetFiringPoint(out Vector3 point)
        {
            point = firingPoint;
            return hasFiringPoint && perception.HasTarget;
        }

        public bool AcquireRangedToken() => AttackTokens.TryAcquire(perception.Target, this, AttackKind.Ranged);

        public void ReleaseRangedToken() => AttackTokens.Release(perception.Target, this, AttackKind.Ranged);

        // ---- events ----

        private void HandlePerceptionChanged()
        {
            bool has = perception.HasTarget;
            if (has && !hadTarget) Raise(EnemyDecision.Spotted);
            else if (!has && hadTarget) Raise(EnemyDecision.LostTarget);
            hadTarget = has;

            if (!has) ReleaseToken();
            if (logState)
                Debug.Log($"[EnemyBrain] {name}: target={perception.HasTarget} lastKnown={perception.HasLastKnown}", this);
            Replan();
        }

        private void HandleHealthChanged(float current, float max)
        {
            bool damaged = lastHealth >= 0f && current < lastHealth - 0.001f && current > 0f;
            float lost = damaged && max > 0f ? (lastHealth - current) / max : 0f;
            lastHealth = current;
            if (!damaged || Profile == null) return;

            AddDefensiveness(Profile.defDamage * lost);

            // Hit from beyond our reach: don't stand there, close in or move
            if (IsRangedProfile && perception.HasTarget && TargetDistance > ranged.MaxRange)
            {
                AddDefensiveness(Profile.defOutranged);
                nextFiringCheck = 0f;
                Raise(EnemyDecision.Outranged);
            }
        }

        private void HandleFlinched() => Raise(EnemyDecision.Flinched);

        private void HandleDeath()
        {
            ReleaseToken();
            AnyDied?.Invoke(this);
        }

        private void HandleAllyDied(EnemyBrain other)
        {
            if (other == this || Profile == null) return;
            float r = Profile.defAllyDeathRadius;
            if (Flat(other.transform.position - transform.position).sqrMagnitude <= r * r) AddDefensiveness(Profile.defAllyDeath);
        }

        public void ReleaseToken() => AttackTokens.ReleaseAll(this);

        // Cover from the current target, kept until the target can see it again.
        public bool TryGetCover(out Vector3 point)
        {
            point = coverPoint;
            if (!WantsCover || perception.Target == null) return false;

            Vector3 threat = AITargets.FeetOf(perception.Target);
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

        // ---- setup checks (debug markers) ----

        public static void CollectIssues(GameObject go, EnemyBehaviourProfileSO profile, List<string> issues)
        {
            var nav = go.GetComponent<NavMeshAgent>();
            if (nav == null) issues.Add("No NavMeshAgent");
            else if (Application.isPlaying && nav.isActiveAndEnabled && !nav.isOnNavMesh) issues.Add("Not on the NavMesh");

            if (profile == null) return;
            if (!profile.melee && !profile.kamikaze && !profile.ranged) issues.Add("Profile has no way to attack");
            if (profile.melee && go.GetComponent<EnemyMeleeAttack>() == null) issues.Add("Melee profile, no EnemyMeleeAttack");
            if (profile.ranged)
            {
                var r = go.GetComponent<EnemyRangedAttack>();
                if (r == null) issues.Add("Ranged profile, no EnemyRangedAttack");
                else r.CollectIssues(issues);
                if (go.GetComponentInChildren<EnemyShooterAnimator>() == null) issues.Add("Ranged profile, no EnemyShooterAnimator");
            }
        }

        public void CollectIssues(List<string> issues)
        {
            CollectIssues(gameObject, Profile != null ? Profile : ResolveProfile(), issues);
            if (LastPointSearchFailed) issues.Add("No firing point with sight of the target");
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

#if UNITY_EDITOR
        private static readonly List<string> gizmoIssues = new List<string>();

        // Edit-time marker: red sphere and the list of setup problems above the enemy
        private void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            gizmoIssues.Clear();
            var def = GetComponent<EnemyDefinitionApplier>();
            var prof = def != null && def.BehaviourProfile != null ? def.BehaviourProfile : defaultProfile;
            CollectIssues(gameObject, prof, gizmoIssues);
            if (gizmoIssues.Count == 0) return;

            Vector3 top = transform.position + Vector3.up * 2.3f;
            Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.9f);
            Gizmos.DrawSphere(top, 0.15f);
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.Label(top + Vector3.up * 0.2f, string.Join("\n", gizmoIssues));
        }
#endif
    }
}
