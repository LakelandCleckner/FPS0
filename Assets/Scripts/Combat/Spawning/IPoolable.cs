namespace Combat.Spawning
{
    // Implemented by any component on a pooled enemy that carries runtime state which
    // must be reset between lives. The pool calls OnSpawn after activating an instance
    // and OnDespawn before returning it. This is the enemy-level twin of the
    // HitContext reset discipline: every stateful component declares how it resets, so
    // a reused enemy can never come back with a stale burn, half health, or a latched
    // IsDying — the enemy equivalent of the WasCrit bug.
    //
    // Keep implementations idempotent and allocation-free; they run every spawn.
    public interface IPoolable
    {
        // Fresh-life setup: clear state to a just-born enemy. Health to full, IsDying
        // false, status pools empty, AI plan/goal cleared, memory wiped.
        void OnSpawn();

        // Pre-return teardown: release anything that shouldn't persist in the pool.
        // Most components only need OnSpawn; OnDespawn is for things that must stop
        // immediately on death (unregister from a manager, halt a coroutine).
        void OnDespawn();
    }
}
