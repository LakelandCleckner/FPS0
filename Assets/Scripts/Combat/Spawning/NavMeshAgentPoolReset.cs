using UnityEngine;
using UnityEngine.AI;

namespace Combat.Spawning
{
    // NavMeshAgent can't implement IPoolable itself (it's a built-in), so this adapter
    // resets it on reuse. Sample-then-warp: on spawn, find the nearest valid NavMesh
    // point to where the pool placed the transform, warp the agent there, and drop any
    // path from its last life.
    //
    // This also fixes a latent spawn bug beyond reuse: SpawnGroup scatters enemies at a
    // flat Y with no NavMesh check, so on uneven ground some land slightly off the mesh
    // and the agent misbehaves. Sampling on spawn snaps them onto the mesh, so it
    // hardens first-spawn placement as well as reuse.
    [RequireComponent(typeof(NavMeshAgent))]
    public class NavMeshAgentPoolReset : MonoBehaviour, IPoolable
    {
        [Tooltip("How far to search for a valid NavMesh point around the spawn position.")]
        [SerializeField] private float sampleRadius = 5f;

        private NavMeshAgent agent;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        public void OnSpawn()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (agent == null) return;

            // The pool has already positioned the transform (position set BEFORE
            // SetActive, before this runs). Sample the mesh near that position and warp
            // the agent onto it, syncing the agent's internal position and clearing the
            // desync a reused agent would otherwise carry.
            Vector3 target = transform.position;
            if (NavMesh.SamplePosition(target, out var hit, sampleRadius, NavMesh.AllAreas))
                target = hit.position;

            // Warp places the agent without pathing; it returns false if the point
            // isn't on the mesh, so the sample above matters on uneven ground.
            agent.Warp(target);

            // Drop any path/velocity carried from the previous life, and make sure the
            // agent isn't left in a stopped state from a prior action.
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
                agent.isStopped = false;
            }
        }

        public void OnDespawn()
        {
            // Stop the agent so a deactivating enemy doesn't leave residual velocity/
            // path that a reused instance might briefly express before OnSpawn runs.
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
        }
    }
}
