using UnityEngine;
using UnityEngine.AI;
using Combat.Core;
using Combat.Feedback;

namespace Combat.Spawning
{
    // One coordinator for an enemy's pooling concerns: death-to-pool return, nav reset,
    // and damage-number reset with a per-life generation. Replaces PooledEnemy,
    // NavMeshAgentPoolReset, and AccumulatorPoolReset. (WanderMarker stays separate — it
    // is a wander-system flag that merely implements IPoolable, not a pooling component.)
    //
    // Merging also fixes an ordering ambiguity: as separate components the pool called
    // their OnSpawns in GetComponents order. Here OnSpawn runs them in a defined order.
    [RequireComponent(typeof(CombatantHealth))]
    public class EnemyPoolAdapter : MonoBehaviour, IPoolable
    {
        [Tooltip("NavMesh search radius when snapping the agent to a valid point on spawn.")]
        [SerializeField] private float navSampleRadius = 5f;

        private CombatantHealth health;
        private NavMeshAgent agent;
        private ICombatant combatant;

        private EnemySpawner spawner;
        private string typeId;
        private EnemyPoolHandle handle;
        private bool returning;

        // Per-life counter. The accumulator registry reads this off the target to drop
        // numbers left from a previous life of this pooled (same-instance) enemy.
        public int Generation { get; private set; }

        private void Awake()
        {
            health = GetComponent<CombatantHealth>();
            agent = GetComponent<NavMeshAgent>();
            combatant = GetComponent<ICombatant>();
        }

        private void OnEnable()
        {
            if (health != null) health.OnDeath += HandleDeath;
            returning = false;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDeath -= HandleDeath;
        }

        // Pool tells a spawned instance where home is.
        public void Bind(EnemySpawner spawner, string typeId, EnemyPoolHandle handle)
        {
            this.spawner = spawner;
            this.typeId = typeId;
            this.handle = handle;
        }

        // ---- IPoolable ----

        public void OnSpawn()
        {
            // New life first, so any number created from here on carries the new gen.
            Generation++;

            ClearAccumulator();
            ResetNavAgent();
        }

        public void OnDespawn()
        {
            ClearAccumulator();

            // Stop the agent so a deactivating enemy leaves no residual path/velocity
            // for a reused instance to briefly express before OnSpawn runs.
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
        }

        // ---- death -> pool ----

        private void HandleDeath()
        {
            if (returning) return;
            returning = true;

            // Deferred-return seam: today immediate. A death animation, or a status
            // transfer reading this corpse's still-live status pools, delays the return
            // here later. On-death reactions have already run (killing hit's Reaction
            // phase completes before Die fires OnDeath).
            if (spawner != null && handle != null)
                spawner.Despawn(typeId, handle);
            else
                Destroy(gameObject);   // not pooled -> still disappears
        }

        // ---- reset helpers ----

        private void ClearAccumulator()
        {
            if (combatant == null) combatant = GetComponent<ICombatant>();
            if (combatant != null && DamageAccumulatorRegistry.Instance != null)
                DamageAccumulatorRegistry.Instance.ClearTarget(combatant);
        }

        private void ResetNavAgent()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (agent == null) return;

            // Transform is already at the spawn position (pool sets it before OnSpawn).
            // Sample the mesh nearby and warp onto it, syncing the agent and hardening
            // off-mesh scatter spawns.
            Vector3 target = transform.position;
            if (NavMesh.SamplePosition(target, out var hit, navSampleRadius, NavMesh.AllAreas))
                target = hit.position;

            agent.Warp(target);

            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
                agent.isStopped = false;
            }
        }

        // Registry helper: current generation for a target, or 0 for non-pooled things
        // (which never reuse, so their numbers never mismatch).
        public static int GetGeneration(ICombatant target)
        {
            var mb = target as MonoBehaviour;
            if (mb == null) return 0;
            var adapter = mb.GetComponent<EnemyPoolAdapter>();
            return adapter != null ? adapter.Generation : 0;
        }
    }
}
