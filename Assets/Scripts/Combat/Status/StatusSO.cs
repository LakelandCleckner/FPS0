using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Stateless status recipe. Holds the authored config; produces configured
    // StatusInstances. Stage 1: tick spec, duration, interval. Category +
    // stacking config arrive in stage 2.

    public enum StatusDurationMode { PerEntryIndependent, RefreshAll, ExtendShared }
    public enum StatusEvictionStrategy { LowestWeight, ShortestRemaining }
    public abstract class StatusSO : ScriptableObject
    {
        [Header("Tick Damage")]
            public DamageDerivation derivation = DamageDerivation.PercentOfWeapon;
            public float coefficient = 0.2f;
            public DamageTypeSO damageType;

        [Header("Timing")]
            public float duration = 3f;
            public float tickInterval = 0.5f;

        [Header("Stack Cap")]
            [Tooltip("Max entries in the pool. 0 = uncapped.")]
            public int maxEntries = 0;
            [Tooltip("LowestWeight: a stronger application pushes out the weakest entry " +
                     "(scorch 'kick the weakest').\n" +
                     "ShortestRemaining: evict the entry closest to expiring, so a long " +
                     "DOT isn't lost to cap pressure.")]
            public StatusEvictionStrategy evictionStrategy = StatusEvictionStrategy.LowestWeight;



        [Header("Duration Behaviour")]
            [Tooltip("PerEntryIndependent: each application expires on its own timer.\n" +
                     "RefreshAll: any new application resets ALL entries' timers.\n" +
                     "ExtendShared: one shared pool timer that applications extend.")]
            public StatusDurationMode durationMode = StatusDurationMode.PerEntryIndependent;

        [Header("ExtendShared settings (only used in ExtendShared mode)")]
            [Tooltip("If true, each application extends by the status's own 'duration'. " +
                     "If false, extends by 'extendAmount'.")]
            public bool extendByOriginalDuration = true;
            [Tooltip("Extension per application when extendByOriginalDuration is false.")]
            public float extendAmount = 1f;
            [Tooltip("Max total remaining time the shared timer can hold (0 = uncapped).")]
            public float extensionCap = 0f;
        
        [Header("Damage Number Presentation")]
            [Tooltip("Spawn an individual floating number each tick.")]
            public bool showFloatingNumber = true;
            [Tooltip("Feed this effect's damage into a rolling accumulator number.")]
            public bool feedsAccumulator = true;

        public DamageSpec BuildTickSpec()
        {
            return new DamageSpec(derivation, coefficient, damageType,
                                  DerivationTiming.SnapshotAtApply);
        }
    }
    
}
