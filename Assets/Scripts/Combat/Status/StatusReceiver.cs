using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Lives on each enemy/target. Owns that target's EffectStackPools, keyed by
    // StatusSO so any source applying the same status stacks into one pool.
    //
    // Phase 2f: the snapshot passed in at apply-time is DamageStats (source-agnostic)
    // instead of the retired StatBlock.
    [RequireComponent(typeof(EnemyHealth))]
    public class StatusReceiver : MonoBehaviour
    {
        private ITargetInfo target;

        private readonly Dictionary<StatusSO, EffectStackPool> pools
            = new Dictionary<StatusSO, EffectStackPool>();

        private void Awake()
        {
            target = GetComponent<EnemyHealth>();
        }

        public void Apply(
            StatusSO status,
            IHitResolver resolver,
            DamageStats stats,
            DamageTypeSO tickType,
            int sourceFaction,
            int chainDepth,
            System.Action<float> applyTickDamage)
        {
            if (target == null || target.IsDying) return;

            if (!pools.TryGetValue(status, out var pool))
            {
                pool = new EffectStackPool();
                pool.Init(target, status, resolver, stats, tickType, applyTickDamage);
                pools[status] = pool;
                StatusManager.Instance.Register(pool);
            }

            float weight = pool.ComputeWeight(stats);
            pool.AddEntry(weight, tickType, sourceFaction, chainDepth);
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