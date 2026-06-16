using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Runtime state of one active status. Now routes each tick THROUGH the
    // resolver (builds a HitContext with Source = StatusTick) so ticks get
    // feedback and can trigger chains — instead of the old direct-damage
    // callback. Per-tick context allocation accepted for now (option 1);
    // isolated to BuildTickContext so it can be pooled/reused later if profiling
    // ever flags it.
    public class StatusInstance
    {
        public ITargetInfo Target;
        public IHitResolver Resolver;
        public StatBlock Stats;
        public DamageSpec TickSpec;
        public List<IHitEffect> CarriedEffects;
        public int SourceFaction;
        public DamageTypeSO TickType;
        // the status definition that produced this instance — used to key the
        // damage accumulator and (later) the per-status tick sound
        public StatusSO SourceStatusDef;
        // how a tick deals damage to the concrete target (set by applier) —
        // mirrors the direct-hit ApplyDamageToTarget so resistance/bodypart work
        public System.Action<float> ApplyTickDamage;
        public float RemainingDuration;
        public float TickInterval;
        private float tickAccumulator;
        public bool Expired { get; private set; }
        public void Init(
            ITargetInfo target,
            IHitResolver resolver,
            StatBlock stats,
            DamageSpec tickSpec,
            List<IHitEffect> carriedEffects,
            int sourceFaction,
            DamageTypeSO tickType,
            StatusSO sourceStatusDef,
            float duration,
            float tickInterval,
            System.Action<float> applyTickDamage)
        {
            Target = target;
            Resolver = resolver;
            Stats = stats;
            TickSpec = tickSpec;
            CarriedEffects = carriedEffects;
            SourceFaction = sourceFaction;
            TickType = tickType;
            SourceStatusDef = sourceStatusDef;
            ApplyTickDamage = applyTickDamage;
            RemainingDuration = duration;
            TickInterval = Mathf.Max(0.01f, tickInterval);
            tickAccumulator = 0f;
            Expired = false;
            DoTick(); // tick-on-apply
        }
        public void Tick(float scaledDelta)
        {
            if (Expired) return;
            RemainingDuration -= scaledDelta;
            tickAccumulator += scaledDelta;
            while (tickAccumulator >= TickInterval && !Expired)
            {
                tickAccumulator -= TickInterval;
                DoTick();
            }
            if (RemainingDuration <= 0f)
                Expired = true;
        }
        private void DoTick()
        {
            if (Target == null || Target.IsDying) { Expired = true; return; }
            // Route through the resolver so the tick gets feedback + can chain.
            var ctx = BuildTickContext();
            Resolver.ResolveHit(ctx);
        }
        // Isolated allocation point (option 1). Swap to a reused/pooled context
        // here later if profiling shows GC pressure — nothing else changes.
        private HitContext BuildTickContext()
        {
            return new HitContext
            {
                Target = Target,
                Source = HitSource.StatusTick,
                SourceFaction = SourceFaction,
                DamageType = TickType,
                // ticks have no body part / positional crit for now (inert hooks
                // exist on the context for a later hitbox-inheritance feature)
                HitboxMultiplier = 1f,
                // status identity + presentation flags for the damage-number
                // system (floater vs rolling accumulator), carried from the def
                SourceStatus = SourceStatusDef,
                ShowFloatingNumber = SourceStatusDef != null ? SourceStatusDef.showFloatingNumber : true,
                FeedsAccumulator = SourceStatusDef != null ? SourceStatusDef.feedsAccumulator : false,
                Stats = Stats,
                // a tick's "effect" is just its own damage application; carried
                // effects are for chain propagation later
                Effects = new List<IHitEffect> { new StatusTickDamageEffect(TickSpec, ApplyTickDamage) },
                MaxChainDepth = 0,
                ChainFalloff = 1f,
                ChainGrowth = 1f,
                DedupMode = HitDedupMode.None
            };
        }
        public void Reset()
        {
            Target = null; Resolver = null; CarriedEffects = null;
            ApplyTickDamage = null; TickType = null; SourceStatusDef = null; Expired = true;
        }
    }
}