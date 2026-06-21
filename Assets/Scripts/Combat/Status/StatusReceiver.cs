using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Lives on each enemy/target. Owns that target's EffectStackPools, keyed by
    // StatusSO so any source applying the same status stacks into one pool.
    // Applies new entries (with dying-guard), registers pools with the manager,
    // and cleans up on death/disable.
    //
    // STAGE 2: pools instead of a flat instance list. Per-application WEIGHT is
    // snapshotted here from the stats passed in at apply-time, so a mid-stack
    // damage change is captured per entry.
    [RequireComponent(typeof(EnemyHealth))]
    public class StatusReceiver : MonoBehaviour
    {
        private ITargetInfo target;

        // one pool per status type on this target
        private readonly Dictionary<StatusSO, EffectStackPool> pools
            = new Dictionary<StatusSO, EffectStackPool>();

        private void Awake()
        {
            target = GetComponent<EnemyHealth>();
        }

        // Apply one application of a status. Finds or creates the pool, snapshots
        // this application's weight from the CURRENT stats, adds the entry.
        public void Apply(
            StatusSO status,
            IHitResolver resolver,
            StatBlock stats,
            DamageTypeSO tickType,
            int sourceFaction,
            int chainDepth,
            System.Action<float> applyTickDamage)
        {
            if (target == null || target.IsDying) return; // dying-guard

            if (!pools.TryGetValue(status, out var pool))
            {
                pool = new EffectStackPool();
                pool.Init(target, status, resolver, stats, tickType, applyTickDamage);
                pools[status] = pool;
                StatusManager.Instance.Register(pool);
            }

            // snapshot THIS application's weight from the stats it arrived with
            float weight = pool.ComputeWeight(stats);
            pool.AddEntry(weight, tickType, sourceFaction, chainDepth);
        }

        // Called by the manager when a pool expires, so the receiver drops it.
        public void OnPoolExpired(EffectStackPool pool)
        {
            if (pool.Status != null) pools.Remove(pool.Status);
        }

        private void OnDisable()
        {
            if (StatusManager.Instance == null) return;
            foreach (var kv in pools)
                StatusManager.Instance.Unregister(kv.Value);
            pools.Clear();
        }
    }
}