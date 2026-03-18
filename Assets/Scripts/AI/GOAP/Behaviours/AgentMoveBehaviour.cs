using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
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
                shouldMove = false;
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
                if (currentTarget != null)
                    nav.SetDestination(currentTarget.Position);
            };

            agent.Events.OnTargetLost += () =>
            {
                currentTarget = null;
                shouldMove = false;
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

            nav.isStopped = !shouldMove;
        }

        private void OnDrawGizmos()
        {
            if (currentTarget != null)
                Gizmos.DrawLine(transform.position, currentTarget.Position);
        }
    }
}