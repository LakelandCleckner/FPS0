namespace Combat.Core
{
    // Describes HOW to compute a damage number, not the number itself.
    // An effect holds one of these and resolves it against a context.
    public readonly struct DamageSpec
    {
        public readonly DamageDerivation Derivation;
        public readonly float Coefficient;   // 0.5 = 50% of whatever the derivation points at
        public readonly DamageTypeSO Type;
        public readonly DerivationTiming Timing;
        public readonly bool AffectedByChainFalloffOverride;
        public readonly bool HasOverride;

        public DamageSpec(DamageDerivation derivation, float coefficient, DamageTypeSO type,
                          DerivationTiming timing = DerivationTiming.SnapshotAtApply,
                          bool? affectedByChainFalloff = null)
        {
            Derivation = derivation;
            Coefficient = coefficient;
            Type = type;
            Timing = timing;
            HasOverride = affectedByChainFalloff.HasValue;
            AffectedByChainFalloffOverride = affectedByChainFalloff ?? false;
        }

        // Default by derivation type (source-anchored = yes, target = no),
        // overridable per spec.
        public bool AffectedByChainFalloff
        {
            get
            {
                if (HasOverride) return AffectedByChainFalloffOverride;
                switch (Derivation)
                {
                    case DamageDerivation.Flat:
                    case DamageDerivation.PercentOfWeapon:
                    case DamageDerivation.PercentOfCrit:
                        return true;
                    default:
                        return false; // target-anchored
                }
            }
        }

        // Raw value BEFORE chain falloff is applied.
        public float ComputeRaw(in StatBlock stats, ITargetInfo target)
        {
            switch (Derivation)
            {
                case DamageDerivation.Flat:                 return Coefficient;
                case DamageDerivation.PercentOfWeapon:      return stats.WeaponDamage * Coefficient;
                case DamageDerivation.PercentOfCrit:        return stats.CritDamage * Coefficient;
                case DamageDerivation.PercentOfTargetMaxHp: return target.MaxHealth * Coefficient;
                case DamageDerivation.PercentOfTargetMissingHp:
                    return (target.MaxHealth - target.CurrentHealth) * Coefficient;
                default: return 0f;
            }
        }
    }
}
