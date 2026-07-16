using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Sources;
using Combat.Stats;

namespace Combat.Status
{
    // Lives on each combatant. Owns that target's EffectStackPools, keyed by StatusSO.
    // The target is the ICombatant (CombatantStats).
    [RequireComponent(typeof(CombatantStats))]
    public class StatusReceiver : MonoBehaviour
    {
        private ICombatant target;

        private readonly Dictionary<StatusSO, EffectStackPool> pools
            = new Dictionary<StatusSO, EffectStackPool>();

        private void Awake()
        {
            target = GetComponent<CombatantStats>();
        }

        public void Apply(
            StatusSO status,
            IHitResolver resolver,
            ICombatant attacker,
            IDamageSource source,
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
                pool.Init(target, status, resolver, attacker, source, tickType, applyTickDamage);
                pools[status] = pool;
                StatusManager.Instance.Register(pool);
            }

            pool.AddEntry(chainMultiplier, tickType, sourceFaction, chainDepth);
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