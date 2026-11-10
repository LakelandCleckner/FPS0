using System;
using UnityEngine;
using UnityEngine.AI;
using Combat.Spawning;

namespace Combat.AI
{
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

        // Target state
        public Transform Target { get; private set; }
        public bool HasTarget { get; private set; }
        public bool TargetVisible { get; private set; }

        // Search state: where the target was last seen and which way it was heading.
        public bool HasLastKnown { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public Vector3 LastKnownHeading { get; private set; }

        // Fired when HasTarget or HasLastKnown flips, so the brain can re-plan straight away.
        public event Action Changed;

        private CombatantHealth health;
        private int sightMask;
        private float nextPerception;
        private float lastContactTime = float.NegativeInfinity;
        private Vector3 lastSeenPosition;
        private Vector3 lastSeenVelocity;
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
            TargetVisible = false;
            Set(false, false);
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
                Set(false, false);
                return;
            }

            if (Target != t) { Target = t; previousTargetPosition = t.position; previousSampleTime = Time.time; }

            if (Time.time >= nextPerception)
            {
                nextPerception = Time.time + PerceptionInterval;
                Perceive();
            }

            if (HasTarget && !Relentless && Time.time - lastContactTime > LoseSightGrace) LoseTarget();
        }

        // One sight check; contact refreshes the last seen data, and noticing engages.
        private void Perceive()
        {
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            Vector3 aim = Target.position + Vector3.up * TargetAimHeight;
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

            MarkContact(Target.position);
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
            Vector3 v = (Target.position - previousTargetPosition) / dt;
            v.y = 0f;
            if (v.sqrMagnitude > 0.04f) lastSeenVelocity = v;
            previousTargetPosition = Target.position;
            previousSampleTime = Time.time;
        }

        private void MarkContact(Vector3 position)
        {
            lastContactTime = Time.time;
            lastSeenPosition = position;
            Set(true, false);
        }

        // Any drop in health means the target hit us: engage and turn on them, even from behind
        private void HandleHealthChanged(float current, float max)
        {
            bool damaged = lastHealth >= 0f && current < lastHealth - 0.001f && current > 0f;
            lastHealth = current;
            if (damaged && Target != null && AITargets.PlayerAlive) MarkContact(Target.position);
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
