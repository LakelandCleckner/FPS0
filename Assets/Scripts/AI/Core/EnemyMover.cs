using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;
using Combat.Enemies;
using Combat.Spawning;
using Combat.Stats;

namespace Combat.AI
{
    // The only thing that drives the NavMeshAgent. Follows the GOAP agent's current target; speed is
    // move_speed x whichever speed factor the running action requested (released when the action ends).
    // Live targets (the target combatant) get the melee approach: rush, stop at contact range, flank
    // round into an open gap when crowded out, and lead the target's movement.
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyMover : MonoBehaviour, IPoolable
    {
        [Header("Movement (pushed as stat bases unless the enemy definition sets them)")]
        [SerializeField] private float baseMoveSpeed = 3.5f;
        [SerializeField] private float chaseSpeedMultiplier = 1.6f;
        [SerializeField] private float investigateSpeedMultiplier = 1.3f;
        [Tooltip("References to the movement stat definitions.")]
        [SerializeField] private EnemyMovementStatKeys movementKeys;

        [Header("Approach")]
        [Tooltip("Stopping distance for point targets (wander, search).")]
        public float StoppingDistance = 1.5f;
        [Tooltip("Avoidance priority once stopped at the target (lower = others steer around it).")]
        public int SettledAvoidancePriority = 30;

        private AgentBehaviour agent;
        private NavMeshAgent nav;
        private CombatantStats stats;
        private EnemyDefinitionApplier definition;
        private EnemyMeleeAttack melee;
        private SurroundRing ring;

        private ITarget currentTarget;
        private bool shouldMove;
        private bool holding;
        private bool facing;          // close to the target: we turn the agent, not the path
        private bool manualRotation;  // an action is turning the agent itself (search look-around)
        private int defaultPriority;
        private float lastAppliedSpeed = -1f;
        private float blockedTime;
        private float flankUntil;
        private float flankBearing;

        private object speedOwner;
        private float speedFactor = 1f;

        // Hold mode: keep a distance from the target and circle (waiting for an attack token).
        private object holdOwner;
        private float holdDistance;
        private float holdCircleSpeed;
        private float holdBearing;
        private float holdDirection = 1f;

        // Look: where the head and aim point (any system may request it); face: turn the body there too
        private object lookOwner;
        private Transform lookTransform;
        private Vector3 lookPoint;
        private object faceOwner;

        // Exact spot an action steers to (cover, peek), overriding the GOAP target
        private const float PointStop = 0.15f;
        private object pointOwner;
        private Vector3 steerPoint;

        // Dodge: a short burst of speed to a spot, over everything else
        private Vector3 dodgePoint;
        private float dodgeSpeed;
        private float dodgeUntil;

        public bool HasLook => lookOwner != null;
        public bool Dodging => Time.time < dodgeUntil;
        public Vector3 LookPoint => lookTransform != null ? lookTransform.position : lookPoint;
        public bool Facing => faceOwner != null;
        public Vector3 Velocity => nav != null ? nav.velocity : Vector3.zero;

        public float BaseMoveSpeed => Stat(movementKeys != null ? movementKeys.moveSpeed : null, baseMoveSpeed);
        public float ChaseFactor => Stat(movementKeys != null ? movementKeys.chaseSpeedMultiplier : null, chaseSpeedMultiplier);
        public float InvestigateFactor => Stat(movementKeys != null ? movementKeys.investigateSpeedMultiplier : null, investigateSpeedMultiplier);

        private void Awake()
        {
            agent = GetComponent<AgentBehaviour>();
            nav = GetComponent<NavMeshAgent>();
            stats = GetComponent<CombatantStats>();
            definition = GetComponent<EnemyDefinitionApplier>();
            melee = GetComponent<EnemyMeleeAttack>();
            nav.stoppingDistance = StoppingDistance;
            defaultPriority = nav.avoidancePriority;
        }

        private void Start()
        {
            ring = FindFirstObjectByType<SurroundRing>();
            PushMovementBases();
        }

        private void OnEnable()
        {
            agent.Events.OnTargetInRange += HandleTargetInRange;
            agent.Events.OnTargetChanged += HandleTargetChanged;
            agent.Events.OnTargetNotInRange += HandleTargetNotInRange;
            agent.Events.OnTargetLost += HandleTargetLost;
        }

        private void OnDisable()
        {
            agent.Events.OnTargetInRange -= HandleTargetInRange;
            agent.Events.OnTargetChanged -= HandleTargetChanged;
            agent.Events.OnTargetNotInRange -= HandleTargetNotInRange;
            agent.Events.OnTargetLost -= HandleTargetLost;
            ResetApproach();
        }

        // ---- IPoolable ----

        public void OnSpawn()
        {
            speedOwner = null;
            speedFactor = 1f;
            holdOwner = null;
            manualRotation = false;
            lookOwner = null;
            lookTransform = null;
            faceOwner = null;
            pointOwner = null;
            dodgeUntil = 0f;
            currentTarget = null;
            shouldMove = false;
            lastAppliedSpeed = -1f;
            ResetApproach();
            ApplyRotationMode();
        }

        public void OnDespawn() { }

        // ---- API for actions ----

        // Last request wins; only the owner that made it can release it.
        public void RequestSpeed(object owner, float factor)
        {
            speedOwner = owner;
            speedFactor = factor;
        }

        public void ReleaseSpeed(object owner)
        {
            if (speedOwner != owner) return;
            speedOwner = null;
            speedFactor = 1f;
        }

        // Keep this distance from the live target and circle at this many degrees per second.
        public void Hold(object owner, float distance, float circleSpeed)
        {
            holdOwner = owner;
            holdDistance = distance;
            holdCircleSpeed = circleSpeed;
            holdDirection = Random.value < 0.5f ? -1f : 1f;
            holdBearing = float.NaN;
        }

        public void ReleaseHold(object owner)
        {
            if (holdOwner == owner) holdOwner = null;
        }

        // An action turning the agent itself (look-around) takes rotation away from the path.
        public void SetManualRotation(bool on)
        {
            manualRotation = on;
            ApplyRotationMode();
        }

        // Last request wins; only the owner that made it can release it.
        public void RequestLook(object owner, Vector3 point)
        {
            lookOwner = owner;
            lookTransform = null;
            lookPoint = point;
        }

        public void RequestLook(object owner, Transform target)
        {
            lookOwner = owner;
            lookTransform = target;
            if (target != null) lookPoint = target.position;
        }

        public void ReleaseLook(object owner)
        {
            if (lookOwner != owner) return;
            lookOwner = null;
            lookTransform = null;
        }

        // Turn the body toward the look point while the path keeps moving the agent (strafing, shooting).
        public void RequestFace(object owner)
        {
            faceOwner = owner;
            ApplyRotationMode();
        }

        public void ReleaseFace(object owner)
        {
            if (faceOwner != owner) return;
            faceOwner = null;
            ApplyRotationMode();
        }

        // Steer to this exact spot until released, whatever the GOAP target is (cover, peek).
        public void RequestPoint(object owner, Vector3 point)
        {
            pointOwner = owner;
            steerPoint = point;
        }

        public void ReleasePoint(object owner)
        {
            if (pointOwner != owner) return;
            pointOwner = null;
            Halt();
        }

        // Dash to this spot at this speed; gives up after maxTime
        public void Dodge(Vector3 point, float speed, float maxTime)
        {
            dodgePoint = point;
            dodgeSpeed = speed;
            dodgeUntil = Time.time + maxTime;
            if (nav.isActiveAndEnabled && nav.isOnNavMesh) nav.SetDestination(point);
        }

        public bool AtPoint(Vector3 point, float tolerance)
        {
            Vector3 d = transform.position - point;
            d.y = 0f;
            return d.sqrMagnitude <= tolerance * tolerance;
        }

        public void Halt()
        {
            if (nav.isActiveAndEnabled && nav.isOnNavMesh) nav.ResetPath();
        }

        // ---- GOAP agent target events ----

        private void HandleTargetInRange(ITarget _)
        {
            // Live targets keep moving; never stop chasing them on "in range"
            if (currentTarget is TransformTarget) return;
            shouldMove = false;
            Halt();
        }

        private void HandleTargetChanged(ITarget t, bool inRange)
        {
            currentTarget = t;
            shouldMove = t is TransformTarget || !inRange;
            if (shouldMove && nav.isActiveAndEnabled && nav.isOnNavMesh) nav.SetDestination(currentTarget.Position);
        }

        private void HandleTargetNotInRange(ITarget _)
        {
            shouldMove = true;
            if (currentTarget != null && nav.isActiveAndEnabled && nav.isOnNavMesh) nav.SetDestination(currentTarget.Position);
        }

        private void HandleTargetLost()
        {
            currentTarget = null;
            ResetApproach();
            shouldMove = false;
            Halt();
        }

        // ---- frame ----

        private void Update()
        {
            // Wind-up: keep closing at a fraction of speed; plant from the first damage frame
            bool swinging = melee != null && melee.IsSwinging;
            bool windupMove = swinging && melee.InWindup && melee.WindupMoveSpeed > 0f;
            bool dodging = Dodging && !swinging;
            if (dodging && AtPoint(dodgePoint, PointStop)) { dodgeUntil = 0f; dodging = false; }
            float desired = dodging ? dodgeSpeed : BaseMoveSpeed * speedFactor * (windupMove ? melee.WindupMoveSpeed : 1f);
            if (!Mathf.Approximately(desired, lastAppliedSpeed))
            {
                nav.speed = desired;
                lastAppliedSpeed = desired;
            }

            if (agent.IsPaused)
            {
                nav.isStopped = true;
                return;
            }

            if (swinging && !windupMove)
            {
                if (!holding && nav.isActiveAndEnabled && nav.isOnNavMesh) nav.velocity = Vector3.zero;
                holding = true;
                nav.isStopped = true;
                return;
            }
            holding = false;

            if (dodging && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                ResetApproach();
                nav.stoppingDistance = PointStop;
                Vector3 off = nav.destination - dodgePoint;
                off.y = 0f;
                if ((!nav.hasPath && !nav.pathPending) || off.sqrMagnitude > 0.04f) nav.SetDestination(dodgePoint);
                nav.isStopped = false;
                return;
            }

            if (pointOwner != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                SteerToPoint();
                return;
            }

            if (shouldMove && currentTarget is TransformTarget && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                nav.SetDestination(Destination(currentTarget.Position));
                nav.isStopped = false;

                // Close in: always face the target; otherwise face it ourselves once the path halts
                Vector3 toTarget = currentTarget.Position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f && (facing || nav.velocity.sqrMagnitude < 0.01f))
                {
                    Quaternion look = Quaternion.LookRotation(toTarget);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, nav.angularSpeed * Time.deltaTime);
                }
                return;
            }

            // A position target can move without a target change (a new firing point): follow it
            if (shouldMove && currentTarget != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                Vector3 off = nav.destination - currentTarget.Position;
                off.y = 0f;
                if (off.sqrMagnitude > 0.25f) nav.SetDestination(currentTarget.Position);
            }

            ResetApproach();
            nav.isStopped = !shouldMove;
        }

        // Exact arrival: tight stopping distance, re-path only when the spot moves
        private void SteerToPoint()
        {
            ResetApproach();
            nav.stoppingDistance = PointStop;
            if (AtPoint(steerPoint, PointStop))
            {
                nav.isStopped = true;
                return;
            }
            Vector3 off = nav.destination - steerPoint;
            off.y = 0f;
            if ((!nav.hasPath && !nav.pathPending) || off.sqrMagnitude > 0.04f) nav.SetDestination(steerPoint);
            nav.isStopped = false;
        }

        // Face mode turns the body after the path has moved it
        private void LateUpdate()
        {
            if (faceOwner == null || lookOwner == null || agent.IsPaused) return;
            if (melee != null && melee.IsSwinging) return;
            Vector3 to = LookPoint - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), nav.angularSpeed * Time.deltaTime);
        }

        // Back to plain movement: path rotation, normal priority, default stopping distance
        private void ResetApproach()
        {
            blockedTime = 0f;
            flankUntil = 0f;
            if (ring != null) ring.SetClose(transform, false);
            if (nav == null) return;
            nav.stoppingDistance = StoppingDistance;
            nav.avoidancePriority = defaultPriority;
            SetFacing(false);
        }

        private void SetFacing(bool on)
        {
            if (on == facing) return;
            facing = on;
            ApplyRotationMode();
        }

        // The path turns the agent only when nothing else does
        private void ApplyRotationMode()
        {
            if (nav != null) nav.updateRotation = !facing && !manualRotation && faceOwner == null;
        }

        // Rush straight at the target and stop at contact range. Held up short of it (crowded out)
        // for a moment: swing round toward its side or back, then rush again.
        private Vector3 Destination(Vector3 target)
        {
            if (holdOwner != null) return HoldPoint(target);
            if (ring == null) return target;

            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;
            float speed = nav.velocity.magnitude;

            bool isClose = dist <= ring.FaceDistance;
            SetFacing(isClose);
            ring.SetClose(transform, isClose);

            bool shortOfTarget = dist > ring.ApproachDistance + 0.2f;
            bool lineBlocked = melee != null && dist <= ring.FlankRange && melee.LineBlocked();
            bool stuck = shortOfTarget && dist <= ring.FlankRange && speed < ring.BlockedSpeed;
            blockedTime = lineBlocked || stuck ? blockedTime + Time.deltaTime : 0f;

            if (blockedTime >= ring.FlankAfter && Time.time >= flankUntil)
            {
                blockedTime = 0f;
                if (ring.FindGap(transform, out float gap))
                {
                    flankBearing = gap;
                    flankUntil = Time.time + ring.FlankHold;
                }
                else if (!lineBlocked)
                {
                    // Nothing wide enough open: swing toward the target's back on this side
                    Vector3 fromTarget = -toTarget;
                    float side = Mathf.Sign(Vector3.SignedAngle(ring.transform.forward, fromTarget, Vector3.up));
                    if (side == 0f) side = Random.value < 0.5f ? -1f : 1f;
                    flankBearing = Bearing(fromTarget) + side * ring.FlankAngle;
                    flankUntil = Time.time + ring.FlankHold;
                }
            }

            bool settled = !shortOfTarget && speed < 0.2f;
            nav.avoidancePriority = settled ? SettledAvoidancePriority : defaultPriority;

            if (Time.time < flankUntil && (shortOfTarget || lineBlocked))
            {
                float a = flankBearing * Mathf.Deg2Rad;
                Vector3 p = target + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ring.ApproachDistance;
                if (NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas))
                {
                    nav.stoppingDistance = 0.3f;
                    return hit.position;
                }
                flankUntil = 0f;
            }

            nav.stoppingDistance = ring.ApproachDistance;
            return Lead(target, dist);
        }

        // A point on a circle around the target, drifting round it, facing the target once there.
        private Vector3 HoldPoint(Vector3 target)
        {
            Vector3 fromTarget = transform.position - target;
            fromTarget.y = 0f;
            if (float.IsNaN(holdBearing)) holdBearing = fromTarget.sqrMagnitude > 0.01f ? Bearing(fromTarget) : Random.Range(0f, 360f);
            holdBearing += holdDirection * holdCircleSpeed * Time.deltaTime;

            if (ring != null) ring.SetClose(transform, false);
            SetFacing(fromTarget.magnitude <= holdDistance + 1.5f);
            nav.avoidancePriority = defaultPriority;
            nav.stoppingDistance = 0.3f;

            float a = holdBearing * Mathf.Deg2Rad;
            Vector3 p = target + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * holdDistance;
            if (NavMesh.SamplePosition(p, out var hit, 1.5f, NavMesh.AllAreas)) return hit.position;

            // Blocked on this side: turn round and circle the other way
            holdDirection = -holdDirection;
            return transform.position;
        }

        // Aim ahead of the target's movement while still closing in
        private Vector3 Lead(Vector3 target, float dist)
        {
            if (ring.MaxLeadTime <= 0f || dist <= ring.ApproachDistance + 0.5f) return target;
            float speed = Mathf.Max(0.1f, nav.speed);
            float t = Mathf.Min(ring.MaxLeadTime, dist / speed);
            Vector3 p = target + ring.Velocity * t;
            return NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas) ? hit.position : target;
        }

        private static float Bearing(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        // ---- stats ----

        private float Stat(StatDefinitionSO def, float fallback)
        {
            var c = stats != null ? stats.Container : null;
            return c == null || def == null ? fallback : c.Resolve(def);
        }

        // Serialized tuning -> stat bases, skipping any the enemy definition owns. Modifiers layer on top.
        private void PushMovementBases()
        {
            var c = stats != null ? stats.Container : null;
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
