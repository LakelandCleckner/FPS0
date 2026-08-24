using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Sources;
using Combat.Stats;
using Combat.Spawning;

namespace Combat.Status
{
    // Lives on each combatant. Owns that target's EffectStackPools, keyed by StatusSO.
    // The target is the ICombatant (CombatantStats).
    [RequireComponent(typeof(CombatantStats))]
    public class StatusReceiver : MonoBehaviour, IPoolable
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
                pool.Owner = this;          // expiry notification without a GetComponent
                pools[status] = pool;
                StatusManager.Instance.Register(pool);
            }
            pool.AddEntry(chainMultiplier, tickType, sourceFaction, chainDepth);
        }
        public void OnPoolExpired(EffectStackPool pool)
        {
            if (pool.Status != null) pools.Remove(pool.Status);
        }

        // ---- IPoolable ----

        // Explicit clear on reuse. OnDisable already clears on deactivate, so a
        // respawned enemy is normally unburned without this — but relying on OnDisable
        // ties the clear to deactivation timing, which a future death animation
        // (deferring deactivation) would loosen. Clearing on spawn guarantees a reused
        // receiver starts empty regardless of when OnDisable ran.
        public void OnSpawn()
        {
            ClearAllPools();
        }

        public void OnDespawn() { }

        private void ClearAllPools()
        {
            if (StatusManager.Instance != null)
                foreach (var kv in pools)
                    StatusManager.Instance.Unregister(kv.Value);
            pools.Clear();
        }

        private void OnDisable()
        {
            if (StatusManager.Instance == null) return;
            foreach (var kv in pools)
                StatusManager.Instance.Unregister(kv.Value);
            pools.Clear();
        }

        // Export every entry of each active pool whose Status is in `filter`, as transfer
        // records. Read-only — doesn't touch this receiver's pools (the victim is about to
        // despawn). Caller supplies the outList (reused per kill).
        public void ExportTransferable(
            System.Collections.Generic.List<StatusSO> filter,
            System.Collections.Generic.List<(StatusSO status, EffectStackPool.TransferEntry entry)> outList)
        {
            if (filter == null || filter.Count == 0) return;
            Debug.Log($"[Transfer] ExportTransferable: victim has {pools.Count} pools, filter has {filter.Count}");
            foreach (var kv in pools)
            {
                Debug.Log($"[Transfer] victim has pool for {kv.Key.name}, in filter={filter.Contains(kv.Key)}");
                if (!filter.Contains(kv.Key)) continue;
                tmpEntries.Clear();
                kv.Value.ExportEntries(tmpEntries);
                Debug.Log($"[Transfer] exported {tmpEntries.Count} entries from {kv.Key.name}");
                for (int i = 0; i < tmpEntries.Count; i++)
                    outList.Add((kv.Key, tmpEntries[i]));
            }
        }


        private readonly System.Collections.Generic.List<EffectStackPool.TransferEntry> tmpEntries
            = new System.Collections.Generic.List<EffectStackPool.TransferEntry>();

        // Import transfer records onto THIS receiver. Ensures a pool per status (creating +
        // registering like Apply), then inserts each entry preserving remaining duration.
        // Builds the tick delegate bound to THIS target with the record's DamageType, so
        // transferred ticks damage the right enemy with the right type — same pattern and
        // lifetime safety as ApplyStatusHitEffect.
        public void ImportTransferred(
            System.Collections.Generic.List<(StatusSO status, EffectStackPool.TransferEntry entry)> records,
            IHitResolver resolver)
        {
            if (target == null || target.IsDying) return;
            if (records == null) return;

            for (int i = 0; i < records.Count; i++)
            {
                var status = records[i].status;
                var rec = records[i].entry;
                var tickType = rec.DamageType;

                if (!pools.TryGetValue(status, out var pool))
                {
                    // Bind the tick delegate to THIS target (not a pooled context), type
                    // from the record. Mirrors ApplyStatusHitEffect.
                    var victim = target;
                    System.Action<float> applyTick =
                        (dmg) => victim.TakeDamage(dmg, BodyPart.Torso, tickType);

                    pool = new EffectStackPool();
                    pool.Init(target, status, resolver, rec.Attacker, rec.Source,
                              tickType, applyTick);
                    pool.Owner = this;
                    pools[status] = pool;
                    StatusManager.Instance.Register(pool);
                }

                pool.ImportEntry(rec, status.transferResetsTickCadence);
            }
        }

    }
}