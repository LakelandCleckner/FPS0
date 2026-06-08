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
        public DamageType damageType = DamageType.Fire;

        [Header("Timing")]
        public float duration = 3f;
        public float tickInterval = 0.5f;

        public DamageSpec BuildTickSpec()
        {
            return new DamageSpec(derivation, coefficient, damageType,
                                  DerivationTiming.SnapshotAtApply);
        }
    }
}
