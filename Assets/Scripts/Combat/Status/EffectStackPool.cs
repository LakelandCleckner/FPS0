using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Sources;

namespace Combat.Status
{
    // The stack of one status on one target. Entries are live-linked via
    // DerivationContext (source + attacker + target); the pool carries the ATTACKER
    // as an ICombatant (crit rolls + attacker-scoped tick derivations) and the SOURCE
    // (weight derivation + dead-source fallback).
    //
    // PERF NOTES (this pass):
    //  - The tick HitContext, its effect list, and its StatusSummedTickEffect are all
    //    built ONCE in Init and refilled per tick. This used to be four allocations
    //    per tick per pool. Safe because WeaponHitResolver copies ctx.Effects into a
    //    rented buffer before sorting, and neither ShowFeedback nor the accumulator
    //    retains the context past the call.
    //  - Entry weights are hoisted into locals. StackEntry.Weight is now also cached
    //    per frame, so these are belt-and-braces rather than load-bearing.
    //  - Diagnostic logging routes through StatusLog, which compiles away entirely
    //    (including the string interpolation at the call site) unless STATUS_DEBUG is
    //    defined in Player Settings > Scripting Define Symbols.
    public class EffectStackPool
    {
        public ICombatant Target { get; private set; }
        public StatusSO Status { get; private set; }
        public IHitResolver Resolver { get; private set; }

        // The receiver that owns this pool. Cached so StatusManager doesn't cast +
        // GetComponent on every expiry. Assigned by StatusManager.Register if the
        // receiver hasn't set it already.
        public StatusReceiver Owner { get; set; }

        private ICombatant attacker;
        private IDamageSource source;
        private System.Action<float> applyTickDamage;
        private DamageTypeSO tickType;

        private readonly List<StackEntry> entries = new List<StackEntry>();

        private float baseTickInterval;
        private float tickAccumulator;
        private float sharedTimer;

        // reused per-tick resolution payload — see PERF NOTES
        private HitContext tickContext;
        private readonly List<IHitEffect> tickEffects = new List<IHitEffect>(1);
        private StatusSummedTickEffect tickEffect;

        public bool Empty => entries.Count == 0;
        public bool Expired { get; private set; }

        public int EntryCount => entries.Count;

        public float SummedWeight
        {
            get { float s = 0f; for (int i = 0; i < entries.Count; i++) s += entries[i].Weight; return s; }
        }

        public void Init(
            ICombatant target,
            StatusSO status,
            IHitResolver resolver,
            ICombatant attacker,
            IDamageSource source,
            DamageTypeSO tickType,
            System.Action<float> applyTickDamage)
        {
            Target = target;
            Status = status;
            Resolver = resolver;
            this.attacker = attacker;
            this.source = source;
            this.tickType = tickType;
            this.applyTickDamage = applyTickDamage;

            baseTickInterval = Mathf.Max(0.01f, status.tickInterval);
            tickAccumulator = 0f;
            sharedTimer = 0f;
            Expired = false;

            BuildReusableTickPayload();
        }

        // Build the tick context / effect list / effect instance once. Fields that
        // never vary for this pool are set here; BuildTickContext only refreshes the
        // ones that change per tick.
        private void BuildReusableTickPayload()
        {
            tickEffect = new StatusSummedTickEffect(0f, tickType, applyTickDamage);

            tickEffects.Clear();
            tickEffects.Add(tickEffect);

            tickContext = new HitContext
            {
                Target = Target,
                Source = HitSource.StatusTick,

                // Carry the originating weapon onto the tick so per-weapon event routing
                // works for DOTs. Without this, ticks publish with a null weapon and any
                // weapon-scoped perk silently never hears the burn it applied.
                DamageSource = source,

                DamageType = tickType,
                HitboxMultiplier = 1f,
                SourceStatus = Status,
                ShowFloatingNumber = Status.showFloatingNumber,
                FeedsAccumulator = Status.feedsAccumulator,

                Attacker = attacker,   // -> resolver rolls crit for this tick

                Effects = tickEffects,
                MaxChainDepth = 0,
                ChainFalloff = 1f,
                ChainGrowth = 1f,
                DedupMode = HitDedupMode.None
            };
        }

        public void AddEntry(float chainMultiplier, DamageTypeSO type, int sourceFaction, int chainDepth)
        {
            bool wasEmpty = entries.Count == 0;

            var e = new StackEntry();
            e.Set(source, attacker, Target, Status.BuildTickSpec(),
                  chainMultiplier, type, sourceFaction, chainDepth, Status.duration);
            entries.Add(e);

            switch (Status.durationMode)
            {
                case StatusDurationMode.PerEntryIndependent:
                    break;
                case StatusDurationMode.RefreshAll:
                    for (int i = 0; i < entries.Count; i++)
                        entries[i].RemainingDuration = Status.duration;
                    break;
                case StatusDurationMode.ExtendShared:
                    float add = Status.extendByOriginalDuration
                        ? Status.duration : Status.extendAmount;
                    sharedTimer += add;
                    if (Status.extensionCap > 0f)
                        sharedTimer = Mathf.Min(sharedTimer, Status.extensionCap);
                    break;
            }

            if (Status.maxEntries > 0 && entries.Count > Status.maxEntries)
                EvictOne();

            StatusLog.Log($"[StackPool] +entry {Status.name} | weight {e.Weight:F1} | now {entries.Count}/{(Status.maxEntries > 0 ? Status.maxEntries.ToString() : "inf")}" + (wasEmpty ? " (first)" : ""));

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
                    float lowest = entries[0].Weight;
                    for (int i = 1; i < entries.Count; i++)
                    {
                        float w = entries[i].Weight;
                        if (w < lowest) { lowest = w; idx = i; }
                    }
                    break;
                case StatusEvictionStrategy.ShortestRemaining:
                    if (Status.durationMode == StatusDurationMode.ExtendShared)
                        idx = 0;
                    else
                        for (int i = 1; i < entries.Count; i++)
                            if (entries[i].RemainingDuration < entries[idx].RemainingDuration) idx = i;
                    break;
            }

            StatusLog.Log($"[StackPool] EVICT {Status.name} | strategy {Status.evictionStrategy} | removed weight {entries[idx].Weight:F1} (rem {entries[idx].RemainingDuration:F1})");
            entries.RemoveAt(idx);
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

            if (Status.tickTimerMode == StatusTickTimerMode.PerEntryTimer)
            {
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

        private void DoSharedTick()
        {
            if (Target == null || Target.IsDying) { Expired = true; return; }

            float damage = CurrentTickDamage();
            if (damage <= 0f) return;

            StatusLog.Log($"[StackPool] TICK(shared) {Status.name} | entries: {entries.Count} | dmg: {damage:F1} | interval: {CurrentInterval():F2} | {Status.intensityMode}/{Status.durationMode}");

            Resolver.ResolveHit(BuildTickContext(damage));
        }

        private void FireEntryTick(StackEntry entry)
        {
            if (Target == null || Target.IsDying) { Expired = true; return; }

            // hoisted: was read three times, each rebuilding a DerivationContext
            float weight = entry.Weight;
            if (weight <= 0f) return;

            StatusLog.Log($"[StackPool] TICK(per-entry) {Status.name} | weight: {weight:F1} | entries: {entries.Count}");

            Resolver.ResolveHit(BuildTickContext(weight));
        }

        // Refills the reusable context. Every field an effect or the resolver WRITES
        // must be reset here, or state leaks from the previous tick.
        //
        // CritMultiplier and WasCrit are reset by WeaponHitResolver.RollCrit, which
        // runs before any effect. Everything else is accumulated by effects and is
        // cleared below.
        private HitContext BuildTickContext(float tickDamage)
        {
            tickContext.BumpGeneration();

            tickEffect.SetTickDamage(tickDamage);

            tickContext.DamageDealt = 0f;
            tickContext.WasKill = false;
            tickContext.WasHeadshot = false;
            tickContext.WasDebuffed = false;
            tickContext.ChainDepth = 0;
            tickContext.ResetAlreadyHit();

            return tickContext;
        }

        private float CurrentInterval()
        {
            if (Status.intensityMode == StatusIntensityMode.Magnitude)
                return baseTickInterval;
            int extra = Mathf.Max(0, entries.Count - 1);
            float reduced = baseTickInterval - Status.intervalReductionPerStack * extra;
            return Mathf.Max(Status.minInterval, reduced);
        }

        private float CurrentTickDamage()
        {
            if (Status.intensityMode == StatusIntensityMode.Rate)
                return entries.Count > 0 ? entries[entries.Count - 1].Weight : 0f;

            float s = 0f;
            for (int i = 0; i < entries.Count; i++) s += entries[i].Weight;
            return s;
        }

        public void ClearAll()
        {
            entries.Clear();
            Expired = true;
        }
    }
}