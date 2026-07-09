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

        public bool AffectedByChainFalloff
        {
            get
            {
                if (HasOverride) return AffectedByChainFalloffOverride;
                switch (Derivation)
                {
                    case DamageDerivation.Flat:
                    case DamageDerivation.PercentOfWeapon:
                        return true;
                    default:
                        return false; // target-anchored
                }
            }
        }

        // Raw value BEFORE chain falloff is applied. Reads the source-agnostic
        // DamageStats snapshot.
        //
        // NOTE: PercentOfCrit was removed in the stat-system migration (Phase 2f).
        // Crit is a PLAYER stat now, not a weapon/source scalar — crit damage will
        // be applied at the resolver (Phase 2h) reading the player's resolved
        // crit_damage, not via a source snapshot. If a "percent of crit damage"
        // derivation is wanted later, it reads the player container, not DamageStats.
        public float ComputeRaw(in DamageStats stats, ITargetInfo target)
        {
            switch (Derivation)
            {
                case DamageDerivation.Flat: return Coefficient;
                case DamageDerivation.PercentOfWeapon: return stats.BaseDamage * Coefficient;
                case DamageDerivation.PercentOfTargetMaxHp: return target.MaxHealth * Coefficient;
                case DamageDerivation.PercentOfTargetMissingHp:
                    return (target.MaxHealth - target.CurrentHealth) * Coefficient;
                default: return 0f;
            }
        }
    }
}