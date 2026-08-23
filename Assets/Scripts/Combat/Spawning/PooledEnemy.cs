using UnityEngine;

namespace Combat.Spawning
{
    // The one component that knows an enemy belongs to a pool and how it goes home.
    // The pool stamps it on spawn (Bind); it listens for the enemy's death and returns
    // the instance to the pool. Nothing else — health, AI, stats — needs a pool
    // reference; they stay pooling-agnostic and this is the single seam.
    //
    // DEFERRED RETURN by design. Death fires an event; this decides WHEN the instance
    // actually returns. Right now that's immediate, but the hook is here so a death
    // animation, or the status-transfer read (which must happen while the corpse's
    // status pools are still alive), can delay the return without touching any other
    // component. Return -> pool.Return -> per-component OnDespawn/OnSpawn reset.
    [RequireComponent(typeof(CombatantHealth))]
    public class PooledEnemy : MonoBehaviour
    {
        private EnemySpawner spawner;
        private string typeId;
        private EnemyPoolHandle handle;
        private CombatantHealth health;
        private bool returning;

        private void Awake()
        {
            health = GetComponent<CombatantHealth>();
        }

        private void OnEnable()
        {
            // Re-subscribe each activation — a pooled instance is enabled/disabled
            // repeatedly, and the subscription must be live for THIS life.
            if (health != null) health.OnDeath += HandleDeath;
            returning = false;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDeath -= HandleDeath;
        }

        // Called by the pool on spawn to tell this instance where home is.
        public void Bind(EnemySpawner spawner, string typeId, EnemyPoolHandle handle)
        {
            this.spawner = spawner;
            this.typeId = typeId;
            this.handle = handle;
        }

        private void HandleDeath()
        {
            if (returning) return;      // death can only fire the return once
            returning = true;

            // DEFERRED-RETURN SEAM. Today: return immediately. Later: a death animation
            // coroutine, or waiting until a status-transfer has read this corpse's live
            // status pools, goes HERE — the return is delayed, and only after it does
            // the pool reset/deactivate the instance. On-death reactions (explosion,
            // and eventually the transfer) have ALREADY run by now: they fire in the
            // killing hit's Reaction phase, which completes before Die() and this event.
            ReturnNow();
        }

        private void ReturnNow()
        {
            if (spawner != null && handle != null)
            {
                spawner.Despawn(typeId, handle);
            }
            else
            {
                // Not pooled (spawned some other way, or Bind never called) — fall back
                // to destroy so a non-pooled enemy still disappears on death.
                Destroy(gameObject);
            }
        }
    }
}
