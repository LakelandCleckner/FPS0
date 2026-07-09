namespace Combat.Stats
{
    // When a modifier source is active, is its contribution live?
    public enum ActivityScope
    {
        InHands,    // only while the weapon is actively wielded
        InLoadout   // while in the loadout, even holstered (holster perks, secondary
                    // weapon passives). Removed entirely on inventory removal.
    }

    // A registered modifier (GDD 14 pt4, pt6). Contributes to `BucketId` of
    // `TargetStat`. Its value is EITHER fixed, OR DERIVED from another stat:
    //
    //   fixed:   Value
    //   derived: min(Cap, Coefficient × resolved(SourceStat))
    //
    // Derivation lives on the MODIFIER, never on a base stat (matches D4, e.g.
    // Earthen Devastation: crit-damage bonus increased by 20% of your CC-damage
    // bonus, up to 40%). Resolution computes derived values lazily at resolve time.
    public class StatModifier
    {
        public readonly StatDefinitionSO TargetStat;
        public readonly string BucketId;
        public readonly ActivityScope Scope;
        public ModifierHandle Handle;

        // fixed value (used when not derived)
        public readonly float Value;

        // derivation (optional)
        public readonly bool IsDerived;
        public readonly StatDefinitionSO SourceStat;   // stat whose resolved value feeds this
        public readonly float Coefficient;             // contribution = coeff × resolved(source)
        public readonly bool UseCap;
        public readonly float Cap;                     // optional max on the contribution

        // fixed-value constructor
        public StatModifier(StatDefinitionSO targetStat, string bucketId, float value,
                            ActivityScope scope = ActivityScope.InHands)
        {
            TargetStat = targetStat;
            BucketId = bucketId;
            Value = value;
            Scope = scope;
            IsDerived = false;
        }

        // derived constructor
        public StatModifier(StatDefinitionSO targetStat, string bucketId,
                            StatDefinitionSO sourceStat, float coefficient,
                            bool useCap = false, float cap = 0f,
                            ActivityScope scope = ActivityScope.InHands)
        {
            TargetStat = targetStat;
            BucketId = bucketId;
            Scope = scope;
            IsDerived = true;
            SourceStat = sourceStat;
            Coefficient = coefficient;
            UseCap = useCap;
            Cap = cap;
        }
    }
}