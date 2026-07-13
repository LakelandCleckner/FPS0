using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Sources;
using Combat.Stats;

namespace Combat.Status
{
    // Lives on each enemy/target. Owns that target's EffectStackPools, keyed by
    // StatusSO so any source applying the same status stacks into one pool.
    //
    // Phase 2h: carries the ATTACKER's stats (so ticks can crit) and the SOURCE
    // (so entries live-link to it for cached weight derivation).
    [RequireComponent(typeof(EnemyHealth))]
    public class StatusReceiver : MonoBehaviour
    {
        private ICombatant target;

        private readonly Dictionary<StatusSO, EffectStackPool> pools
            = new Dictionary<StatusSO, EffectStackPool>();

        private void Awake()
        {
            target = GetComponent<EnemyHealth>();
        }

        // Apply one application of a status.
        //   source        — live link for the entry's weight (nullable)
        //   snapshotStats — fallback if the source dies/disappears
        //   attackerStats — the attacker's player-scope stats (crit) for tick rolls
        public void Apply(
            StatusSO status,
            IHitResolver resolver,
            IDamageSource source,
            DamageStats snapshotStats,
            StatContainer attackerStats,
            DamageTypeSO tickType,
            int sourceFaction,
            int chainDepth,
            float chainMultiplier,
            System.Action<float> applyTickDamage)
        {
            if (target == null || target.IsDying) return;

            if (!pools.TryGetValue(status, out var pool))
            {
                pool = new EffectStackPool();
                pool.Init(target, status, resolver, attackerStats, tickType, applyTickDamage);
                pools[status] = pool;
                StatusManager.Instance.Register(pool);
            }

            pool.AddEntry(source, snapshotStats, chainMultiplier, tickType, sourceFaction, chainDepth);
        }

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