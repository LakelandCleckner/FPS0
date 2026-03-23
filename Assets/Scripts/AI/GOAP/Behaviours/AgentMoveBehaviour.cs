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
        public float MoveSpeed = 3.5f;

        // Must match the In Range value set on WanderAction in the capability asset
        public float StoppingDistance = 1.5f;

        private AgentBehaviour agent;
        private NavMeshAgent nav;
        private ITarget currentTarget;
        private bool shouldMove;

        private void Awake()
        {
            agent = GetComponent<AgentBehaviour>();
            nav = GetComponent<NavMeshAgent>();
            nav.speed = MoveSpeed;
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