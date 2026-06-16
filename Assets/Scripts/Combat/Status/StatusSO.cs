using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Stateless status recipe. Holds the authored config; produces configured
    // StatusInstances. Stage 1: tick spec, duration, interval. Category +
    // stacking config arrive in stage 2.
    public abstract class StatusSO : ScriptableObject
    {
        [Header("Tick Damage")]
        public DamageDerivation derivation = DamageDerivation.PercentOfWeapon;
        public float coefficient = 0.2f;
        public DamageTypeSO damageType;

        [Header("Timing")]
        public float duration = 3f;
        public float tickInterval = 0.5f;

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
