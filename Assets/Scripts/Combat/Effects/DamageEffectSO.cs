using UnityEngine;
using Combat.Core;
using Combat.Stats;

namespace Combat.Effects
{
    // Concrete effect definition: flat / stat / quantity damage. Inspector-authored
    // DamageSpec (data-driven). Produces a (stateless) DamageHitEffect.
    [CreateAssetMenu(fileName = "DamageEffect", menuName = "Combat/Effects/Damage")]
    public class DamageEffectSO : HitEffectSO
    {
        [Header("Damage Derivation")]
        [Tooltip("Flat: fixed number. PercentOfStat: % of a stat. " +
                 "PercentOfQuantity: % of a runtime quantity (health, ...).")]
        public DerivationKind derivationKind = DerivationKind.PercentOfStat;
        [Tooltip("Whose stat/quantity to read.")]
        public StatScope owner = StatScope.Source;
        [Tooltip("PercentOfStat: which stat (e.g. weapon_damage on Source).")]
        public StatDefinitionSO stat;
        [Tooltip("PercentOfQuantity: which runtime quantity.")]
        public QuantityKind quantity = QuantityKind.CurrentHealth;
        [Tooltip("Multiplier on whatever the derivation points at (1 = 100%).")]
        public float coefficient = 1f;
        public DamageTypeSO damageType;

        [Header("Chain")]
        [Tooltip("Leave unchecked to use the per-derivation default (Target-scoped " +
                 "derivations don't fall off; others do).")]
        public bool overrideChainFalloff = false;
        public bool affectedByChainFalloff = true;

        protected override IHitEffect Build()
        {
            bool? chainOverride = overrideChainFalloff ? affectedByChainFalloff : (bool?)null;

            DamageSpec spec;
            switch (derivationKind)
            {
                case DerivationKind.Flat:
                    spec = new DamageSpec(coefficient, damageType, chainOverride);
                    break;
                case DerivationKind.PercentOfQuantity:
                    spec = new DamageSpec(quantity, owner, coefficient, damageType, chainOverride);
                    break;
                case DerivationKind.PercentOfStat:
                default:
                    spec = new DamageSpec(stat, owner, coefficient, damageType, chainOverride);
                    break;
            }
            return new DamageHitEffect(spec);
        }
    }
}