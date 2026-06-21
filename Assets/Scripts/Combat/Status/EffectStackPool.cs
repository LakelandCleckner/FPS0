using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // The stack of one status on one target — the unit that ticks. Replaces
    // StatusInstance: owns the tick timer, the entry collection, the snapshot
    // stats, and routes ticks through the resolver. A tick sums the current
    // entries' weights for its magnitude (so more applications = bigger tick on
    // the SAME cadence, not more frequent ticks — step 1 uses one shared timer).
    //
    // STEP 2 added the DURATION AXIS (Status.durationMode).
    // STEP 3 adds CAP + EVICTION (Status.maxEntries / evictionStrategy) and
    // per-application WEIGHT SNAPSHOT (each entry captures the damage at the
    // moment it was applied, so a mid-stack damage change is preserved per entry
    // and lowest-weight eviction has real differences to act on).
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

        private float tickInterval;
        private float tickAccumulator;

        // ExtendShared mode only: the single shared remaining timer
        private float sharedTimer;

        public bool Empty => entries.Count == 0;
        public bool Expired { get; private set; }

        // debug accessors (for an optional overlay)
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

            tickInterval = Mathf.Max(0.01f, status.tickInterval);
            tickAccumulator = 0f;
            sharedTimer = 0f;
            Expired = false;
        }

        // Add one application's entry. The WEIGHT is computed by the caller at
        // apply-time (per-application snapshot) and passed in, so a mid-stack
        // damage change is captured per entry. First entry into an empty pool
        // ticks on apply; later entries just raise magnitude for the next
        // scheduled tick (conditional tick-on-apply).
        public void AddEntry(float weight, DamageTypeSO type, int sourceFaction, int chainDepth)
        {
            bool wasEmpty = entries.Count == 0;

            var e = new StackEntry();
            e.Set(weight, type, sourceFaction, chainDepth, Status.duration);
            entries.Add(e);

            // DURATION AXIS — adjust timers based on the status's mode
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

            // CAP + EVICTION — if over the cap, evict one entry by the strategy.
            // New entry added first, so it's a candidate to KEEP (but could be the
            // one evicted if it's itself the weakest/shortest).
            if (Status.maxEntries > 0 && entries.Count > Status.maxEntries)
                EvictOne();

            Debug.Log($"[StackPool] +entry {Status.name} | weight {weight:F1} | now {entries.Count}/{(Status.maxEntries > 0 ? Status.maxEntries.ToString() : "inf")}" + (wasEmpty ? " (first)" : ""));

            if (wasEmpty)
                DoTick();
        }

        // Remove one entry per the configured strategy.
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
                    // ExtendShared entries share one timer -> use insertion order
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

        // Compute one application's tick weight from CURRENT stats + the spec.
        public float ComputeWeight(StatBlock atApplyStats)
        {
            return Status.BuildTickSpec().ComputeRaw(atApplyStats, Target);
        }

        public void Tick(float scaledDelta)
        {
            if (Expired) return;

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

            tickAccumulator += scaledDelta;
            while (tickAccumulator >= tickInterval && !Expired)
            {
                tickAccumulator -= tickInterval;
                DoTick();
                if (entries.Count == 0) { Expired = true; return; }
            }
        }

        private void DoTick()
        {
            if (Target == null || Target.IsDying) { Expired = true; return; }

            float summed = 0f;
            for (int i = 0; i < entries.Count; i++)
                summed += entries[i].Weight;

            if (summed <= 0f) return;

            Debug.Log($"[StackPool] TICK {Status.name} | entries: {entries.Count} | summed: {summed:F1} | mode: {Status.durationMode}");

            var ctx = BuildTickContext(summed);
            Resolver.ResolveHit(ctx);
        }

        private HitContext BuildTickContext(float summedWeight)
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
                Effects = new List<IHitEffect> { new StatusSummedTickEffect(summedWeight, tickType, applyTickDamage) },
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