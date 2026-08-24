using UnityEngine;
using Combat.Core;
using Combat.Stats;

namespace Combat.Status
{
    public enum StatusDurationMode { PerEntryIndependent, RefreshAll, ExtendShared }
    public enum StatusEvictionStrategy { LowestWeight, ShortestRemaining }
    public enum StatusIntensityMode { Magnitude, Rate, Both }
    public enum StatusTickTimerMode { SharedPoolTimer, PerEntryTimer }

    // Stateless status recipe. Phase 2i-b: the tick's derivation is authored as a
    // KIND + owner scope + stat/quantity, matching the data-driven DamageSpec.
    public abstract class StatusSO : ScriptableObject
    {
        [Header("Tick Damage — derivation")]
        [Tooltip("Flat: fixed. PercentOfStat: % of a stat. PercentOfQuantity: % of a runtime quantity (health, ...).")]
        public DerivationKind derivationKind = DerivationKind.PercentOfStat;
        [Tooltip("Whose stat/quantity the tick reads.")]
        public StatScope owner = StatScope.Source;
        [Tooltip("PercentOfStat: which stat (e.g. weapon_damage on Source).")]
        public StatDefinitionSO stat;
        [Tooltip("PercentOfQuantity: which runtime quantity.")]
        public QuantityKind quantity = QuantityKind.CurrentHealth;
        [Tooltip("Multiplier applied to whatever the derivation points at (0.2 = 20%).")]
        public float coefficient = 0.2f;
        public DamageTypeSO damageType;

        [Header("Timing")]
        public float duration = 3f;
        public float tickInterval = 0.5f;

        [Header("Stack Cap")]
        [Tooltip("Max entries in the pool. 0 = uncapped.")]
        public int maxEntries = 0;
        public StatusEvictionStrategy evictionStrategy = StatusEvictionStrategy.LowestWeight;

        [Header("Duration Behaviour")]
        public StatusDurationMode durationMode = StatusDurationMode.PerEntryIndependent;

        [Header("ExtendShared settings (only used in ExtendShared mode)")]
        public bool extendByOriginalDuration = true;
        public float extendAmount = 1f;
        public float extensionCap = 0f;

        [Header("Intensity (how stacks scale the DOT)")]
        public StatusIntensityMode intensityMode = StatusIntensityMode.Magnitude;
        public float intervalReductionPerStack = 0f;
        public float minInterval = 0.1f;

        [Header("Tick Timer")]
        public StatusTickTimerMode tickTimerMode = StatusTickTimerMode.SharedPoolTimer;

        [Header("Damage Number Presentation")]
        public bool showFloatingNumber = true;
        public bool feedsAccumulator = true;

        [Header("On-Kill Reactions")]
        [Tooltip("Effects that run when a TICK of this status kills the target. Drop an " +
         "Explosion On Kill here and burn-kills detonate. Independent of any " +
         "explosion the weapon itself carries, so they're balanced separately. " +
         "These run in the tick's Reaction phase — they check WasKill themselves.")]
        public System.Collections.Generic.List<Combat.Effects.HitEffectSO> onKillEffects
            = new System.Collections.Generic.List<Combat.Effects.HitEffectSO>();

        [Header("Transfer")]
        [Tooltip("When this status transfers to a new target (e.g. via an explosion), " +
         "reset its tick cadence. OFF (default) preserves the tick timer so a SLOW " +
         "DOT doesn't sit for a full interval before its first tick on the new " +
         "target — avoids a dead beat. ON restarts the cadence cleanly, better for " +
         "fast DOTs where a near-immediate carried-over tick would feel off.")]
        public bool transferResetsTickCadence = false;



        // Build the tick's DamageSpec from the authored derivation.
        public DamageSpec BuildTickSpec()
        {
            switch (derivationKind)
            {
                case DerivationKind.Flat:
                    return new DamageSpec(coefficient, damageType);
                case DerivationKind.PercentOfQuantity:
                    return new DamageSpec(quantity, owner, coefficient, damageType);
                case DerivationKind.PercentOfStat:
                default:
                    return new DamageSpec(stat, owner, coefficient, damageType);
            }
        }
    }
}