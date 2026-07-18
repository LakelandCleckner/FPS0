using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Behaviours
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentMoveBehaviour : MonoBehaviour
    {

        // Must match the In Range value set on WanderAction in the capability asset
        public float StoppingDistance = 1.5f;

        private AgentBehaviour agent;
        private NavMeshAgent nav;
        private AgentBrain brain;
        private ITarget currentTarget;
        private bool shouldMove;
        private float lastAppliedSpeed = -1f;

        private void Awake()
        {
            agent = GetComponent<AgentBehaviour>();
            nav = GetComponent<NavMeshAgent>();
            brain = GetComponent<AgentBrain>();

            nav.stoppingDistance = StoppingDistance;
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
                shouldMove = !inRange;
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
            float desiredSpeed = brain.BaseMoveSpeed * brain.CurrentSpeedMultiplier;
            if (!Mathf.Approximately(desiredSpeed, lastAppliedSpeed))
            {
                nav.speed = desiredSpeed;
                lastAppliedSpeed = desiredSpeed;
            }

            if (Time.frameCount % 60 == 0)
                Debug.Log($"[Move] shouldMove={shouldMove} target={(currentTarget == null ? "null" : currentTarget.GetType().Name)} speed={nav.speed:F2} stopped={nav.isStopped} paused={agent.IsPaused}");

            if (agent.IsPaused)
            {
                nav.isStopped = true;
                return;
            }

            // Continuously update destination for live targets like the player
            if (shouldMove && currentTarget is TransformTarget && nav.isActiveAndEnabled && nav.isOnNavMesh)
            {
                nav.SetDestination(currentTarget.Position);
                nav.isStopped = false;

                // Inside stopping distance the agent halts, and NavMesh rotation only turns it
                // while moving — so face the target ourselves or it freezes staring one way.
                Vector3 toTarget = currentTarget.Position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f && nav.velocity.sqrMagnitude < 0.01f)
                {
                    Quaternion look = Quaternion.LookRotation(toTarget);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, look, nav.angularSpeed * Time.deltaTime);
                }
                return;
            }

            nav.isStopped = !shouldMove;
        }

        private void OnDrawGizmos()
        {
            if (currentTarget != null)
                Gizmos.DrawLine(transform.position, currentTarget.Position);
        }
    }
}