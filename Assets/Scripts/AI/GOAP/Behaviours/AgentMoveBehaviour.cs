using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;
using Combat.Enemies;

namespace GOAPGettingStarted.Behaviours
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentMoveBehaviour : MonoBehaviour
    {

        // Must match the In Range value set on WanderAction in the capability asset
        public float StoppingDistance = 1.5f;
        [Tooltip("Avoidance priority once stopped at the player (lower = others steer around it).")]
        public int SettledAvoidancePriority = 30;

        private AgentBehaviour agent;
        private NavMeshAgent nav;
        private AgentBrain brain;
        private EnemyMeleeAttack melee;
        private bool holding;
        private ITarget currentTarget;
        private bool shouldMove;
        private float lastAppliedSpeed = -1f;
        private SurroundRing ring;
        private bool facing;     // close to the player: we turn the agent, not the path
        private int defaultPriority;
        private float blockedTime;
        private float flankUntil;
        private float flankBearing;

        private void Awake()
        {
            agent = GetComponent<AgentBehaviour>();
            nav = GetComponent<NavMeshAgent>();
            brain = GetComponent<AgentBrain>();
            melee = GetComponent<EnemyMeleeAttack>();

            nav.stoppingDistance = StoppingDistance;
            defaultPriority = nav.avoidancePriority;
        }

        private void Start() => ring = FindFirstObjectByType<SurroundRing>();

        private void OnDisable() => ResetApproach();

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
            nav.updateRotation = !on;
        }

        private void OnEnable()
        {
            agent.Events.OnTargetInRange += _ =>
            {
                //Don't stop moving for TransformTargets - player will move 
                if (currentTarget is TransformTarget)
                    return;

                shouldMove = false;
                if (nav.isActiveAndEnabled && nav.isOnNavMesh)
                    nav.ResetPath();
            };

            agent.Events.OnTargetChanged += (t, inRange) =>
            {
                currentTarget = t;
                // A live target (the player) moves, so never stop chasing it on "in range"
                shouldMove = t is TransformTarget || !inRange;
                if (shouldMove)
                    nav.SetDestination(currentTarget.Position);
            };

            agent.Events.OnTargetNotInRange += _ =>
            {
                shouldMove = true;
                if (currentTarget != null && nav.isActiveAndEnabled && nav.isOnNavMesh)
                    nav.SetDestination(currentTarget.Position);
            };

            agent.Events.OnTargetLost += () =>
            {
                currentTarget = null;
                ResetApproach();
                shouldMove = false;
                if (nav.isActiveAndEnabled && nav.isOnNavMesh)
                    nav.ResetPath();
            };
        }

        private void Update()
        {
            // Speed resolves from stats (cached); only write to the agent when it actually
            // changes, so a slow/buff lands the frame it's applied without a native write
            // every frame.
            // Wind-up: keep chasing at a fraction of speed; plant from the first damage frame
            bool swinging = melee != null && melee.IsSwinging;
            bool windupMove = swinging && melee.InWindup && melee.WindupMoveSpeed > 0f;
            float desiredSpeed = brain.BaseMoveSpeed * brain.CurrentSpeedMultiplier
                                 * (windupMove ? melee.WindupMoveSpeed : 1f);
            if (desiredSpeed == 0f && Time.frameCount % 30 == 0)
                Debug.Log($"[Move] FROZEN: base={brain.BaseMoveSpeed} mult={brain.CurrentSpeedMultiplier}");
            if (!Mathf.Approximately(desiredSpeed, lastAppliedSpeed))
            {
                nav.speed = desiredSpeed;
                lastAppliedSpeed = desiredSpeed;
            }

            //if (Time.frameCount % 60 == 0)
                //Debug.Log($"[Move] shouldMove={shouldMove} target={(currentTarget == null ? "null" : currentTarget.GetType().Name)} speed={nav.speed:F2} stopped={nav.isStopped} paused={agent.IsPaused}");

            if (agent.IsPaused)
            {
                nav.isStopped = true;
                return;
            }

            // Plant feet from the first damage frame to the end of the swing; EnemyMeleeAttack handles turning.
            if (swinging && !windupMove)
            {
                if (!holding && nav.isActiveAndEnabled && nav.isOnNavMesh)
                    nav.velocity = Vector3.zero;
                holding = true;
                nav.isStopped = true;
                return;
            }
            holding = false;

            // Continuously update destination for live targets like the player
            if (shouldMove && currentTarget is TransformTarget && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                nav.SetDestination(Destination(currentTarget.Position));
                nav.isStopped = false;

                // Close in: always face the player. Otherwise NavMesh rotation only turns the
                // agent while moving, so face the target ourselves once it halts.
                Vector3 toTarget = currentTarget.Position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f && (facing || nav.velocity.sqrMagnitude < 0.01f))
                {
                    Quaternion look = Quaternion.LookRotation(toTarget);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, look, nav.angularSpeed * Time.deltaTime);
                }
                return;
            }

            ResetApproach();
            nav.isStopped = !shouldMove;
        }

        // Rush straight at the player and stop at contact range. Held up short of it (crowded out)
        // for a moment: swing round toward the player's side or back, then rush again.
        private Vector3 Destination(Vector3 target)
        {
            if (ring == null) return target;

            Vector3 toPlayer = target - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            float speed = nav.velocity.magnitude;

            bool isClose = dist <= ring.FaceDistance;
            SetFacing(isClose);
            ring.SetClose(transform, isClose);

            // Held up: another enemy is in the way, or stuck short of the player
            bool shortOfPlayer = dist > ring.ApproachDistance + 0.2f;
            bool lineBlocked = melee != null && dist <= ring.FlankRange && melee.LineBlocked();
            bool stuck = shortOfPlayer && dist <= ring.FlankRange && speed < ring.BlockedSpeed;
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
                    // Nothing wide enough open: fallback swing toward the player's back on this side
                    Vector3 fromPlayer = -toPlayer;
                    float side = Mathf.Sign(Vector3.SignedAngle(ring.transform.forward, fromPlayer, Vector3.up));
                    if (side == 0f) side = Random.value < 0.5f ? -1f : 1f;
                    flankBearing = Bearing(fromPlayer) + side * ring.FlankAngle;
                    flankUntil = Time.time + ring.FlankHold;
                }
                // Blocked with no gap: wait behind the front row
            }

            bool settled = !shortOfPlayer && speed < 0.2f;
            nav.avoidancePriority = settled ? SettledAvoidancePriority : defaultPriority;

            if (Time.time < flankUntil && (shortOfPlayer || lineBlocked))
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

        // Aim ahead of the player's movement while still closing in, so cutting across gets cut off
        private Vector3 Lead(Vector3 target, float dist)
        {
            if (ring.MaxLeadTime <= 0f || dist <= ring.ApproachDistance + 0.5f) return target;
            float speed = Mathf.Max(0.1f, nav.speed);
            float t = Mathf.Min(ring.MaxLeadTime, dist / speed);
            Vector3 p = target + ring.Velocity * t;
            return NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas) ? hit.position : target;
        }

        private static float Bearing(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        private void OnDrawGizmos()
        {
            if (currentTarget != null)
                Gizmos.DrawLine(transform.position, currentTarget.Position);
        }
    }
}