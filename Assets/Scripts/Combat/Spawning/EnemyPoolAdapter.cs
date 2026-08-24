using UnityEngine;
using UnityEngine.AI;
using Combat.Core;
using Combat.Feedback;

namespace Combat.Spawning
{
    // Coordinates an enemy's pooling: death-to-pool return, nav reset, damage-number
    // reset with a per-life generation. The return is deferred to end of frame so the
    // killing hit's full resolution — including the Reaction phase, where an on-kill
    // explosion reads this victim's still-live status pools for transfer — completes
    // before the enemy tears down.
    [RequireComponent(typeof(CombatantHealth))]
    public class EnemyPoolAdapter : MonoBehaviour, IPoolable
    {
        [SerializeField] private float navSampleRadius = 5f;

        private CombatantHealth health;
        private NavMeshAgent agent;
        private ICombatant combatant;

        private EnemySpawner spawner;
        private string typeId;
        private EnemyPoolHandle handle;
        private bool returning;

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

        public void Bind(EnemySpawner spawner, string typeId, EnemyPoolHandle handle)
        {
            this.spawner = spawner;
            this.typeId = typeId;
            this.handle = handle;
        }

        // ---- IPoolable ----

        public void OnSpawn()
        {
            Generation++;
            ClearAccumulator();
            ResetNavAgent();
        }

        public void OnDespawn()
        {
            ClearAccumulator();
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
        }

        // ---- death -> pool (deferred) ----

        private void HandleDeath()
        {
            if (returning) return;
            returning = true;

            // Defer to end of frame so the current hit resolution finishes first —
            // including its Reaction phase, where an on-kill explosion reads this
            // victim's still-live status pools (transfer). Returning synchronously here
            // would clear the pools mid-resolution, before the explosion reads them.
            // The dying enemy is already inert during this window: CombatantHealth and
            // the status ticks both early-out on IsDying, so it takes no more damage and
            // its DOTs stop.
            StartCoroutine(ReturnAtEndOfFrame());
        }

        private System.Collections.IEnumerator ReturnAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();

            if (spawner != null && handle != null)
                spawner.Despawn(typeId, handle);
            else
                Destroy(gameObject);
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

        public static int GetGeneration(ICombatant target)
        {
            var mb = target as MonoBehaviour;
            if (mb == null) return 0;
            var adapter = mb.GetComponent<EnemyPoolAdapter>();
            return adapter != null ? adapter.Generation : 0;
        }
    }
}