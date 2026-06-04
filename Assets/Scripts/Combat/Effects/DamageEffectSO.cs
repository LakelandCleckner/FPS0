using UnityEngine;
using Combat.Core;

namespace Combat.Effects
{
    // Concrete effect definition: flat / derived damage. Inspector-authored
    // DamageSpec. Produces a (stateless) DamageHitEffect.
    [CreateAssetMenu(fileName = "DamageEffect", menuName = "Combat/Effects/Damage")]
    public class DamageEffectSO : HitEffectSO
    {
        [Header("Damage Derivation")]
        public DamageDerivation derivation = DamageDerivation.PercentOfWeapon;
        public float coefficient = 1f;
        public DamageType damageType = DamageType.Physical;
        public DerivationTiming timing = DerivationTiming.SnapshotAtApply;

        [Header("Chain")]
        [Tooltip("Leave unchecked to use the per-derivation default.")]
        public bool overrideChainFalloff = false;
        public bool affectedByChainFalloff = true;

        protected override IHitEffect Build()
        {
            var spec = new DamageSpec(
                derivation,
                coefficient,
                damageType,
                timing,
                overrideChainFalloff ? affectedByChainFalloff : (bool?)null);

            return new DamageHitEffect(spec);
        }
    }
}
