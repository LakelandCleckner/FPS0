using System;
using UnityEngine;
using UnityEngine.AI;
using Combat.Spawning;

namespace Combat.AI
{
    // Awareness ladder, lowest to highest.
    public enum CombatStatus { Idle, Searching, LostSight, Visible, Exposed, Danger }

    // What the enemy knows about its target: noticed by sight (cone, or close by in any facing), damage or gunfire.
    // Once engaged, line of sight alone keeps contact. Relentless enemies never lose an engaged target;
    // others lose it after LoseSightGrace out of sight and keep a last known position to search.
    public class EnemyPerception : MonoBehaviour, IPoolable
    {
        [Header("Sight")]
        public float DetectionRange = 25f;
        public float FieldOfViewAngle = 120f;
        [Tooltip("Within this distance the target is noticed whatever way the enemy faces (line of sight still needed).")]
        public float SenseRadius = 10f;
        [Tooltip("Height of the enemy's eyes above its root.")]
        public float EyeHeight = 1.5f;
        [Tooltip("Height on the target the sight ray aims at.")]
        public float TargetAimHeight = 1.2f;
        [Tooltip("Seconds between sight checks.")]
        public float PerceptionInterval = 0.1f;

        [Header("Awareness")]
        [Tooltip("Seconds out of sight before an engaged enemy loses the target and searches (ignored when Relentless).")]
        public float LoseSightGrace = 5f;
        [Tooltip("Engaged enemies chase until the target is dead and never fall back to searching.")]
        public bool Relentless = true;
        [Tooltip("Gunfire within this distance alerts the enemy (m).")]
        public float HearingRadius = 80f;

        [Header("Status")]
        [Tooltip("Exposed: the target's view is within this many degrees of the enemy.")]
        public float ExposedAngle = 35f;
        [Tooltip("Exposed: a visible target this close always counts as seeing the enemy (m).")]
        public float ExposedDistance = 12f;
        [Tooltip("Aimed at: the target's view is within this many degrees of the enemy.")]
        public float AimedAtAngle = 6f;
        [Tooltip("Danger: target within this distance (m).")]
        public float DangerDistance = 2.5f;
        [Tooltip("Danger: seconds after taking damage.")]
        public float DangerTime = 1f;

        // Target state
        public Transform Target { get; private set; }
        public bool HasTarget { get; private set; }
        public bool TargetVisible { get; private set; }

        // Search state: where the target was last seen and which way it was heading.
        public bool HasLastKnown { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public Vector3 LastKnownHeading { get; private set; }

        // Awareness, worked out every frame
        public CombatStatus Status { get; private set; }
        public bool TargetSeesMe { get; private set; }
        public bool AimedAt { get; private set; }
        public float LastDamagedTime { get; private set; } = float.NegativeInfinity;

        // Where the enemy thinks the target is: its real position only while visible
        public Vector3 BelievedPosition => TargetVisible && Target != null ? TargetFeet : lastSeenPosition;
        public Vector3 TargetVelocity => TargetVisible ? currentVelocity : Vector3.zero;
        public float TimeSinceContact => Time.time - lastContactTime;
        public Vector3 EyePosition => transform.position + Vector3.up * EyeHeight;

        // Target's ground point: the player root sits at its controller's centre
        private Vector3 TargetFeet => AITargets.FeetOf(Target);

        // Fired when HasTarget or HasLastKnown flips, so the brain can re-plan straight away.
        public event Action Changed;
        public event Action<CombatStatus> StatusChanged;
        // Our own sight, hearing or a hit put the target here (not shared contact)
        public event Action<Vector3> Contact;

        private CombatantHealth health;
        private int sightMask;
        private float nextPerception;
        private float lastContactTime = float.NegativeInfinity;
        private Vector3 lastSeenPosition;
        private Vector3 lastSeenVelocity;
        private Vector3 currentVelocity;
        private Transform view;
        private Vector3 previousTargetPosition;
        private float previousSampleTime;
        private float lastHealth = -1f;

        private void Awake()
        {
            health = GetComponent<CombatantHealth>();
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

        // ---- IPoolable ----

        public void OnSpawn()
        {
            lastContactTime = float.NegativeInfinity;
            lastHealth = -1f;
            LastDamagedTime = float.NegativeInfinity;
            TargetVisible = false;
            TargetSeesMe = false;
            AimedAt = false;
            currentVelocity = Vector3.zero;
            Set(false, false);
            SetStatus(CombatStatus.Idle);
        }

        public void OnDespawn() { }

        // ---- frame ----

        private void Update()
        {
            var t = AITargets.Player;
            if (t == null) return;

            if (!AITargets.PlayerAlive)
            {
                TargetVisible = false;
                TargetSeesMe = false;
                AimedAt = false;
                Set(false, false);
                SetStatus(CombatStatus.Idle);
                return;
            }

            if (Target != t) { Target = t; previousTargetPosition = AITargets.FeetOf(t); previousSampleTime = Time.time; }

            if (Time.time >= nextPerception)
            {
                nextPerception = Time.time + PerceptionInterval;
                Perceive();
                SenseExposure();
            }

            if (HasTarget && !Relentless && Time.time - lastContactTime > LoseSightGrace) LoseTarget();
            SetStatus(WorkOutStatus());
        }

        // Highest rung that applies
        private CombatStatus WorkOutStatus()
        {
            if (HasTarget)
            {
                float d = Target != null ? Flat(TargetFeet - transform.position).magnitude : float.MaxValue;
                if (Time.time - LastDamagedTime <= DangerTime || d <= DangerDistance) return CombatStatus.Danger;
                if (!TargetVisible) return CombatStatus.LostSight;
                return TargetSeesMe ? CombatStatus.Exposed : CombatStatus.Visible;
            }
            return HasLastKnown ? CombatStatus.Searching : CombatStatus.Idle;
        }

        // Whether the target's view covers this enemy; sight is symmetric, so it needs our line of sight too
        private void SenseExposure()
        {
            TargetSeesMe = false;
            AimedAt = false;
            if (!TargetVisible) return;

            if (view == null) view = Camera.main != null ? Camera.main.transform : null;
            Vector3 chest = transform.position + Vector3.up * TargetAimHeight;
            float d = Flat(TargetFeet - transform.position).magnitude;
            if (view == null) { TargetSeesMe = d <= ExposedDistance; return; }

            float angle = Vector3.Angle(view.forward, chest - view.position);
            AimedAt = angle <= AimedAtAngle;
            TargetSeesMe = angle <= ExposedAngle || d <= ExposedDistance;
        }

        private void SetStatus(CombatStatus s)
        {
            if (s == Status) return;
            Status = s;
            StatusChanged?.Invoke(s);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // One sight check; contact refreshes the last seen data, and noticing engages.
        private void Perceive()
        {
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            Vector3 aim = TargetFeet + Vector3.up * TargetAimHeight;
            Vector3 to = aim - eye;
            float distance = to.magnitude;

            SampleTargetVelocity();

            TargetVisible = distance <= DetectionRange && HasLineOfSight(eye, to, distance);
            if (!TargetVisible) return;

            Vector3 flatTo = to; flatTo.y = 0f;
            Vector3 flatFwd = transform.forward; flatFwd.y = 0f;
            bool inCone = Vector3.Angle(flatFwd, flatTo) <= FieldOfViewAngle * 0.5f;
            bool sensed = distance <= SenseRadius;

            // Once engaged, line of sight alone keeps contact
            if (!inCone && !sensed && !HasTarget) return;

            MarkContact(TargetFeet);
        }

        // Anything on the target (controller, body, hitboxes) counts; triggers ignored
        private bool HasLineOfSight(Vector3 eye, Vector3 to, float distance)
        {
            if (!Physics.Raycast(eye, to / distance, out var hit, distance, sightMask, QueryTriggerInteraction.Ignore))
                return true;
            return hit.transform == Target || hit.transform.IsChildOf(Target);
        }

        private void SampleTargetVelocity()
        {
            float dt = Time.time - previousSampleTime;
            if (dt <= 0f) return;
            Vector3 v = (TargetFeet - previousTargetPosition) / dt;
            v.y = 0f;
            currentVelocity = v;
            if (v.sqrMagnitude > 0.04f) lastSeenVelocity = v;
            previousTargetPosition = TargetFeet;
            previousSampleTime = Time.time;
        }

        private void MarkContact(Vector3 position)
        {
            lastContactTime = Time.time;
            lastSeenPosition = position;
            Set(true, false);
            Contact?.Invoke(position);
        }

        // A squadmate's contact: engage toward it; never overrides our own sight
        public void ShareContact(Vector3 position)
        {
            if (Target == null || !AITargets.PlayerAlive || TargetVisible) return;
            lastContactTime = Time.time;
            lastSeenPosition = position;
            Set(true, false);
        }

        // Any drop in health means the target hit us: engage and turn on them, even from behind
        private void HandleHealthChanged(float current, float max)
        {
            bool damaged = lastHealth >= 0f && current < lastHealth - 0.001f && current > 0f;
            lastHealth = current;
            if (damaged) LastDamagedTime = Time.time;
            if (damaged && Target != null && AITargets.PlayerAlive) MarkContact(TargetFeet);
        }

        // Gunfire within hearing range: engage and turn toward it
        private void HandleNoise(Vector3 position, Transform source)
        {
            if (Target == null || !AITargets.PlayerAlive) return;
            Vector3 d = position - transform.position;
            if (d.sqrMagnitude > HearingRadius * HearingRadius) return;
            MarkContact(position);
        }

        // Out of contact too long: remember where they were, then search there.
        private void LoseTarget()
        {
            Vector3 pos = lastSeenPosition;
            if (NavMesh.SamplePosition(pos, out var hit, 5f, NavMesh.AllAreas)) pos = hit.position;
            else pos.y = transform.position.y;

            LastKnownPosition = pos;
            Vector3 toPos = pos - transform.position; toPos.y = 0f;
            LastKnownHeading = lastSeenVelocity.sqrMagnitude > 0.01f ? lastSeenVelocity.normalized : toPos.normalized;
            Set(false, true);
        }

        // The search reached the last known position and looked around.
        public void ClearLastKnown() => Set(HasTarget, false);

        private void Set(bool hasTarget, bool hasLastKnown)
        {
            if (hasTarget == HasTarget && hasLastKnown == HasLastKnown) return;
            HasTarget = hasTarget;
            HasLastKnown = hasLastKnown;
            Changed?.Invoke();
        }
    }
}
