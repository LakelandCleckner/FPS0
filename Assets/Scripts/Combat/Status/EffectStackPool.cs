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
    // STEP 1 SCOPE: applications add entries; tick sums entries; per-entry
    // independent duration expiry; pool empties when no entries remain.
    // (Cap/eviction = step 3, intensity/timer axes = steps 4-5, layered on later.)
    public class EffectStackPool
    {
        public ITargetInfo Target { get; private set; }
        public StatusSO Status { get; private set; }
        public IHitResolver Resolver { get; private set; }

        // snapshot stats from the first application (ticks derive from this)
        private StatBlock stats;
        private System.Action<float> applyTickDamage;
        private DamageTypeSO tickType;

        private readonly List<StackEntry> entries = new List<StackEntry>();

        private float tickInterval;
        private float tickAccumulator;

        public bool Empty => entries.Count == 0;
        public bool Expired { get; private set; }

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
            Expired = false;
        }

        // Add one application's entry. Step 1: snapshot the tick weight now from
        // the status spec + stats, give it the status's duration. First entry into
        // an empty pool ticks on apply; later entries just raise magnitude for the
        // next scheduled tick (the conditional tick-on-apply rule).
        public void AddEntry(float weight, DamageTypeSO type, int sourceFaction, int chainDepth)
        {
            bool wasEmpty = entries.Count == 0;

            var e = new StackEntry();
            e.Set(weight, type, sourceFaction, chainDepth, Status.duration);
            entries.Add(e);

            if (wasEmpty)
                DoTick(); // tick-on-first-apply only
        }

        // Compute one application's tick weight from the snapshot spec + stats.
        // (Exposed so the applier can snapshot at apply-time.)
        public float ComputeWeight()
        {
            return Status.BuildTickSpec().ComputeRaw(stats, Target);
        }

        public void Tick(float scaledDelta)
        {
            if (Expired) return;

            // age entries; drop expired ones (per-entry independent duration)
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                entries[i].RemainingDuration -= scaledDelta;
                if (entries[i].RemainingDuration <= 0f)
                    entries.RemoveAt(i);
            }

            if (entries.Count == 0) { Expired = true; return; }

            // shared-timer tick: fire on interval, dealing the SUMMED weight
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

            Debug.Log($"[StackPool] {Status.name} on {(Target as MonoBehaviour)?.name} | " +
             $"entries: {entries.Count} | summed tick: {summed:F1}");

            var ctx = BuildTickContext(summed);
            Resolver.ResolveHit(ctx);
        }

        // Isolated allocation point (option 1) — poolable later if profiling flags it.
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
                // the tick's damage is the pre-summed weight; a tiny effect applies
                // exactly that (resistance still applied at the chokepoint inside)
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
