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
            pool.AddEntry(source, attacker, chainMultiplier, tickType, sourceFaction, chainDepth);
        }
        public void OnPoolExpired(EffectStackPool pool)
        {
            pool.OnExpiredCleanup();          //lift any stat modifier it applied
            if (pool.Status != null) pools.Remove(pool.Status);
        }


        // ---- IPoolable ----

        // Explicit clear on reuse. OnDisable already clears on deactivate, so a
        // respawned enemy is normally unburned without this -- but relying on OnDisable
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

        // Read-only export, capped per pool; records stay grouped by status
        public void ExportTransferable(
            System.Collections.Generic.List<StatusSO> filter,
            System.Collections.Generic.List<(StatusSO status, EffectStackPool.TransferEntry entry)> outList)
        {
            if (filter == null || filter.Count == 0) return;
            StatusLog.Log($"[Transfer] ExportTransferable: victim has {pools.Count} pools, filter has {filter.Count}");
            foreach (var kv in pools)
            {
                if (!filter.Contains(kv.Key)) continue;
                tmpEntries.Clear();
                kv.Value.ExportEntries(tmpEntries, kv.Value.EntryCap);
                StatusLog.Log($"[Transfer] exported {tmpEntries.Count}/{kv.Value.EntryCount} entries from {kv.Key.name}");
                for (int i = 0; i < tmpEntries.Count; i++)
                    outList.Add((kv.Key, tmpEntries[i]));
            }
        }


        private readonly System.Collections.Generic.List<EffectStackPool.TransferEntry> tmpEntries
            = new System.Collections.Generic.List<EffectStackPool.TransferEntry>();

        // Imports each status run as one batch onto this target
        public void ImportTransferred(
            System.Collections.Generic.List<(StatusSO status, EffectStackPool.TransferEntry entry)> records,
            IHitResolver resolver)
        {
            if (target == null || target.IsDying) return;
            if (records == null) return;

            int i = 0;
            while (i < records.Count)
            {
                var status = records[i].status;
                int runEnd = i + 1;
                while (runEnd < records.Count && records[runEnd].status == status) runEnd++;

                var rec = records[i].entry;
                var tickType = rec.DamageType;

                if (!pools.TryGetValue(status, out var pool))
                {
                    // Bind the tick delegate to THIS target, type
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

                pool.ImportEntries(records, i, runEnd - i, status.transferResetsTickCadence);
                i = runEnd;
            }
        }

    }
}