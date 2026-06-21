using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // The stack of one status on one target — the unit that ticks. Replaces
    // StatusInstance: owns the tick timer(s), the entry collection, the snapshot
    // stats, and routes ticks through the resolver.
    //
    // STEP 2: duration axis (Status.durationMode).
    // STEP 3: cap + eviction (maxEntries / evictionStrategy) + per-application weight.
    // STEP 4: intensity axis (Status.intensityMode: Magnitude / Rate / Both) —
    //         applies to SharedPoolTimer mode.
    // STEP 5: tick-timer axis (Status.tickTimerMode):
    //   SharedPoolTimer (default) -> one timer, intensity axis scales it
    //   PerEntryTimer             -> each entry ticks independently at the base
    //                                interval dealing its own weight (intensity
    //                                axis ignored)
    //
    // NOTE: contains [StackPool] Debug.Log lines for testing — strip for release.
    public class EffectStackPool
    {
        public ITargetInfo Target { get; private set; }
        public StatusSO Status { get; private set; }
        public IHitResolver Resolver { get; private set; }

        private StatBlock stats;
        private System.Action<float> applyTickDamage;
        private DamageTypeSO tickType;

        private readonly List<StackEntry> entries = new List<StackEntry>();

        private float baseTickInterval;
        private float tickAccumulator; // SharedPoolTimer mode

        private float sharedTimer; // ExtendShared duration mode

        public bool Empty => entries.Count == 0;
        public bool Expired { get; private set; }

        public int EntryCount => entries.Count;
        public float SummedWeight
        {
            get { float s = 0f; for (int i = 0; i < entries.Count; i++) s += entries[i].Weight; return s; }
        }

        public void Init(
            ITargetInfo target,
            StatusSO status,
            IHitResolver resolver,
            StatBlock stats,
            DamageTypeSO tickType,
            System.Action<float> applyTickDamage)
        {
            Target = target;
            Status = status;
            Resolver = resolver;
            this.stats = stats;
            this.tickType = tickType;
            this.applyTickDamage = applyTickDamage;

            baseTickInterval = Mathf.Max(0.01f, status.tickInterval);
            tickAccumulator = 0f;
            sharedTimer = 0f;
            Expired = false;
        }

        public void AddEntry(float weight, DamageTypeSO type, int sourceFaction, int chainDepth)
        {
            bool wasEmpty = entries.Count == 0;

            var e = new StackEntry();
            e.Set(weight, type, sourceFaction, chainDepth, Status.duration);
            entries.Add(e);

            // DURATION AXIS
            switch (Status.durationMode)
            {
                case StatusDurationMode.PerEntryIndependent:
                    break;
                case StatusDurationMode.RefreshAll:
                    for (int i = 0; i < entries.Count; i++)
                        entries[i].RemainingDuration = Status.duration;
                    break;
                case StatusDurationMode.ExtendShared:
                    float add = Status.extendByOriginalDuration ? Status.duration : Status.extendAmount;
                    sharedTimer += add;
                    if (Status.extensionCap > 0f)
                        sharedTimer = Mathf.Min(sharedTimer, Status.extensionCap);
                    break;
            }

            // CAP + EVICTION
            if (Status.maxEntries > 0 && entries.Count > Status.maxEntries)
                EvictOne();

            Debug.Log($"[StackPool] +entry {Status.name} | weight {weight:F1} | now {entries.Count}/{(Status.maxEntries > 0 ? Status.maxEntries.ToString() : "inf")}" + (wasEmpty ? " (first)" : ""));

            // tick-on-first-apply
            if (wasEmpty)
            {
                if (Status.tickTimerMode == StatusTickTimerMode.PerEntryTimer)
                    FireEntryTick(entries[entries.Count - 1]);
                else
                    DoSharedTick();
            }
        }

        private void EvictOne()
        {
            if (entries.Count == 0) return;

            int idx = 0;
            switch (Status.evictionStrategy)
            {
                case StatusEvictionStrategy.LowestWeight:
                    for (int i = 1; i < entries.Count; i++)
                        if (entries[i].Weight < entries[idx].Weight) idx = i;
                    break;
                case StatusEvictionStrategy.ShortestRemaining:
                    if (Status.durationMode == StatusDurationMode.ExtendShared)
                        idx = 0;
                    else
                        for (int i = 1; i < entries.Count; i++)
                            if (entries[i].RemainingDuration < entries[idx].RemainingDuration) idx = i;
                    break;
            }

            Debug.Log($"[StackPool] EVICT {Status.name} | strategy {Status.evictionStrategy} | removed weight {entries[idx].Weight:F1} (rem {entries[idx].RemainingDuration:F1})");
            entries.RemoveAt(idx);
        }

        public float ComputeWeight(StatBlock atApplyStats)
        {
            return Status.BuildTickSpec().ComputeRaw(atApplyStats, Target);
        }

        // INTENSITY AXIS — effective shared interval (SharedPoolTimer mode).
        private float CurrentInterval()
        {
            if (Status.intensityMode == StatusIntensityMode.Magnitude)
                return baseTickInterval;
            int extra = Mathf.Max(0, entries.Count - 1);
            float reduced = baseTickInterval - Status.intervalReductionPerStack * extra;
            return Mathf.Max(Status.minInterval, reduced);
        }

        // INTENSITY AXIS — shared tick magnitude (SharedPoolTimer mode).
        private float CurrentTickDamage()
        {
            if (Status.intensityMode == StatusIntensityMode.Rate)
                return entries.Count > 0 ? entries[entries.Count - 1].Weight : 0f;
            float s = 0f;
            for (int i = 0; i < entries.Count; i++) s += entries[i].Weight;
            return s;
        }

        public void Tick(float scaledDelta)
        {
            if (Expired) return;

            // DURATION AXIS — expiry first
            if (Status.durationMode == StatusDurationMode.ExtendShared)
            {
                sharedTimer -= scaledDelta;
                if (sharedTimer <= 0f) { Expired = true; return; }
            }
            else
            {
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    entries[i].RemainingDuration -= scaledDelta;
                    if (entries[i].RemainingDuration <= 0f)
                        entries.RemoveAt(i);
                }
                if (entries.Count == 0) { Expired = true; return; }
            }

            // TICK-TIMER AXIS
            if (Status.tickTimerMode == StatusTickTimerMode.PerEntryTimer)
            {
                // each entry ticks independently at the base interval
                for (int i = 0; i < entries.Count; i++)
                {
                    entries[i].TickAccumulator += scaledDelta;
                    while (entries[i].TickAccumulator >= baseTickInterval)
                    {
                        entries[i].TickAccumulator -= baseTickInterval;
                        FireEntryTick(entries[i]);
                        if (Target == null || Target.IsDying) { Expired = true; return; }
                    }
                }
            }
            else
            {
                // shared timer; intensity axis scales it
                tickAccumulator += scaledDelta;
                float interval = CurrentInterval();
                while (tickAccumulator >= interval && !Expired)
                {
                    tickAccumulator -= interval;
                    DoSharedTick();
                    if (entries.Count == 0) { Expired = true; return; }
                    interval = CurrentInterval();
                }
            }
        }

        // one summed/base tick for the whole pool (SharedPoolTimer)
        private void DoSharedTick()
        {
            if (Target == null || Target.IsDying) { Expired = true; return; }

            float damage = CurrentTickDamage();
            if (damage <= 0f) return;

            Debug.Log($"[StackPool] TICK(shared) {Status.name} | entries: {entries.Count} | dmg: {damage:F1} | interval: {CurrentInterval():F2} | {Status.intensityMode}/{Status.durationMode}");

            Resolver.ResolveHit(BuildTickContext(damage));
        }

        // one tick for a single entry (PerEntryTimer)
        private void FireEntryTick(StackEntry entry)
        {
            if (Target == null || Target.IsDying) { Expired = true; return; }
            if (entry.Weight <= 0f) return;

            Debug.Log($"[StackPool] TICK(per-entry) {Status.name} | weight: {entry.Weight:F1} | entries: {entries.Count}");

            Resolver.ResolveHit(BuildTickContext(entry.Weight));
        }

        private HitContext BuildTickContext(float tickDamage)
        {
            return new HitContext
            {
                Target = Target,
                Source = HitSource.StatusTick,
                DamageType = tickType,
                HitboxMultiplier = 1f,
                SourceStatus = Status,
                ShowFloatingNumber = Status.showFloatingNumber,
                FeedsAccumulator = Status.feedsAccumulator,
                Stats = stats,
                Effects = new List<IHitEffect> { new StatusSummedTickEffect(tickDamage, tickType, applyTickDamage) },
                MaxChainDepth = 0,
                ChainFalloff = 1f,
                ChainGrowth = 1f,
                DedupMode = HitDedupMode.None
            };
        }

        public void ClearAll()
        {
            entries.Clear();
            Expired = true;
        }
    }
}