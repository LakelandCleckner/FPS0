using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Sources;
using Combat.Stats;
using Combat.Spawning;

namespace Combat.Status
{
    // Lives on each combatant. Owns that target's EffectStackPools, keyed by StatusSO.
    // The target is the ICombatant (CombatantStats).
    // Reacts to a status's control effects (stun, stagger, knockback) on the combatant that receives it.
    public interface IStatusControl
    {
        void ApplyControl(StatusSO status, Vector3 from);
    }

    [RequireComponent(typeof(CombatantStats))]
    public class StatusReceiver : MonoBehaviour, IPoolable
    {
        // Receiver per combatant, so hit code can check statuses without GetComponent.
        private static readonly Dictionary<ICombatant, StatusReceiver> registry = new Dictionary<ICombatant, StatusReceiver>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => registry.Clear();

        public static StatusReceiver For(ICombatant c)
            => c != null && registry.TryGetValue(c, out var r) && r != null ? r : null;

        private ICombatant target;
        private readonly Dictionary<StatusSO, EffectStackPool> pools
            = new Dictionary<StatusSO, EffectStackPool>();
        private readonly Dictionary<StatusSO, GameObject> auras = new Dictionary<StatusSO, GameObject>();
        private readonly List<StatusSO> scratch = new List<StatusSO>(4);
        private StatusAura.BodyPoint[] hullPoints;
        private StatusOverlay overlay;

        private StatusOverlay Overlay()
        {
            if (overlay == null)
            {
                overlay = GetComponent<StatusOverlay>();
                if (overlay == null) overlay = gameObject.AddComponent<StatusOverlay>();
                overlay.Bind(this);
            }
            return overlay;
        }
        private StatusAura.BodyPoint[] skinPoints;

        // Active Conductive status, or null.
        public ConductiveStatusSO Conductive { get; private set; }

        private IStatusControl control;

        private void Awake()
        {
            target = GetComponent<CombatantStats>();
            control = GetComponent<IStatusControl>();
        }

        private void OnEnable()
        {
            if (target != null) registry[target] = this;
        }

        private void OnDestroy()
        {
            if (target != null && registry.TryGetValue(target, out var r) && r == this) registry.Remove(target);
        }

        public bool Has(StatusSO status) => status != null && pools.TryGetValue(status, out var p) && !p.Expired;

        // Keeps an active status from expiring (gallery previews).
        public void Hold(StatusSO status)
        {
            if (status != null && pools.TryGetValue(status, out var p) && !p.Expired) p.RefreshDuration();
        }

        // Ends every status now, lifting their stat changes.
        public void ClearStatuses() => ClearAllPools();

        public int Stacks(StatusSO status) => status != null && pools.TryGetValue(status, out var p) && !p.Expired ? p.EntryCount : 0;

        public float Remaining(StatusSO status) => status != null && pools.TryGetValue(status, out var p) && !p.Expired ? p.Remaining : 0f;

        // Fade-out strength for a status's visuals, seeded per enemy.
        public float Fade(StatusSO status) => status != null ? status.FadeAt(Remaining(status), GetInstanceID()) : 1f;

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
            if (target == null || target.IsDying || status == null) return;

            // Can't build toward a conversion that's already active (no Chill while Frozen).
            if (status.convertsTo != null && Has(status.convertsTo)) return;

            if (!pools.TryGetValue(status, out var pool) || pool.Expired)
            {
                pool = new EffectStackPool();
                pool.Init(target, status, resolver, attacker, source, tickType, applyTickDamage);
                pool.Owner = this;          // expiry notification without a GetComponent
                pools[status] = pool;
                StatusManager.Instance.Register(pool);
                OnPoolStarted(status);
                ApplyControl(status, attacker);
            }
            pool.AddEntry(source, attacker, chainMultiplier, tickType, sourceFaction, chainDepth);

            if (status.convertsTo != null && status.convertAtStacks > 0 && pool.EntryCount >= status.convertAtStacks)
            {
                EndPool(status, pool);
                var next = status.convertsTo;
                Apply(next, resolver, attacker, source, next.damageType != null ? next.damageType : tickType,
                      sourceFaction, chainDepth, chainMultiplier, applyTickDamage);
            }
        }

        public void OnPoolExpired(EffectStackPool pool)
        {
            pool.OnExpiredCleanup();          //lift any stat modifier it applied
            var status = pool.Status;
            if (status == null) return;
            // A converted pool was already replaced; leave the new one alone.
            if (!pools.TryGetValue(status, out var current) || !ReferenceEquals(current, pool)) return;
            pools.Remove(status);
            OnPoolEnded(status, true);
        }

        // Ends one status early (conversion). Its stat change lifts now.
        private void EndPool(StatusSO status, EffectStackPool pool)
        {
            if (StatusManager.Instance != null) StatusManager.Instance.Unregister(pool);
            pool.ClearAll();
            pools.Remove(status);
            OnPoolEnded(status, true);
        }

        // Stun, stagger and knockback, pushed away from the attacker
        private void ApplyControl(StatusSO status, ICombatant attacker)
        {
            if (control == null || (status.stunTime <= 0f && !status.stagger && status.knockback <= 0f)) return;
            var a = attacker as Component;
            control.ApplyControl(status, a != null ? a.transform.position : transform.position - transform.forward);
        }

        private void OnPoolStarted(StatusSO status)
        {
            if (status is ConductiveStatusSO c) Conductive = c;
            if (status.overlayMaterial != null) Overlay().Add(status);

            if (status.auraVfx == null) return;
            Color tint = status.tintAura && status.damageType != null ? status.damageType.color : Color.clear;
            var t = transform;
            var fx = VfxPool.Spawn(status.auraVfx, t.TransformPoint(status.auraOffset), t.rotation, t, tint, 0f, status.auraScale, status.colorizeAura);
            if (fx == null) return;
            auras[status] = fx;

            var aura = fx.GetComponent<StatusAura>();
            if (aura == null && (status.auraScalesWithStacks || status.auraEmitFrom != AuraEmitFrom.Prefab)) aura = fx.AddComponent<StatusAura>();
            if (aura != null) aura.Bind(this, status, BodyPoints(status.auraEmitFrom));
        }

        // Emission points for the mode: baked skin points, else hull points sampled once; null for Prefab.
        private StatusAura.BodyPoint[] BodyPoints(AuraEmitFrom mode)
        {
            if (mode == AuraEmitFrom.Prefab) return null;
            if (mode == AuraEmitFrom.Skin)
            {
                if (skinPoints == null)
                {
                    var baked = GetComponent<BodySurfacePoints>();
                    if (baked == null) baked = GetComponentInChildren<BodySurfacePoints>(true);
                    if (baked == null) baked = GetComponentInParent<BodySurfacePoints>();
                    skinPoints = baked != null ? baked.Points : System.Array.Empty<StatusAura.BodyPoint>();
                }
                if (skinPoints.Length > 0) return skinPoints;
            }
            return hullPoints ??= StatusAura.Sample(transform);
        }

        private void OnPoolEnded(StatusSO status, bool playEnd, bool deferRelease = false)
        {
            if (ReferenceEquals(Conductive, status)) Conductive = FindConductive();
            if (overlay != null) overlay.Remove(status);

            if (auras.TryGetValue(status, out var fx))
            {
                // Reparenting is not allowed while this object is being deactivated.
                if (deferRelease) VfxPool.ReleaseLater(fx);
                else VfxPool.Release(fx);
                auras.Remove(status);
            }

            if (playEnd && status.endVfx != null && gameObject.activeInHierarchy)
            {
                Color tint = status.tintAura && status.damageType != null ? status.damageType.color : Color.clear;
                VfxPool.Spawn(status.endVfx, transform.TransformPoint(status.auraOffset), Quaternion.identity, null,
                              tint, status.endVfxLifetime, status.auraScale, status.colorizeAura);
            }
        }

        private ConductiveStatusSO FindConductive()
        {
            foreach (var kv in pools)
                if (kv.Key is ConductiveStatusSO c && !kv.Value.Expired) return c;
            return null;
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
            foreach (var kv in pools)
            {
                kv.Value.ClearAll();   // lifts its stat change too
                if (StatusManager.Instance != null) StatusManager.Instance.Unregister(kv.Value);
            }
            pools.Clear();
            ReleaseAuras();
        }

        private void ReleaseAuras(bool deferRelease = false)
        {
            scratch.Clear();
            scratch.AddRange(auras.Keys);
            for (int i = 0; i < scratch.Count; i++) OnPoolEnded(scratch[i], false, deferRelease);
            auras.Clear();
            Conductive = null;
        }

        private void OnDisable()
        {
            ReleaseAuras(true);
            foreach (var kv in pools) kv.Value.ClearAll();   // lifts its stat change too
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

                if (!pools.TryGetValue(status, out var pool) || pool.Expired)
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
                    OnPoolStarted(status);
                }

                pool.ImportEntries(records, i, runEnd - i, status.transferResetsTickCadence);
                i = runEnd;
            }
        }

    }
}
