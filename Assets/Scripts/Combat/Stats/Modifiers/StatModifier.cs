namespace Combat.Stats
{
    // When a modifier source is active, is its contribution live?
    public enum ActivityScope
    {
        InHands,    // only while the weapon is actively wielded
        InLoadout   // while the item is in the loadout, even holstered (holster perks,
                    // a secondary weapon's passive affixes). Removed entirely on
                    // inventory removal.
    }

    // A registered modifier (GDD 14 §4). Contributes `Value` to `BucketId` of
    // `TargetStat`. Carried in a StatContainer's per-stat list; resolution converts
    // it to a StatModifierValue and runs the bucket math.
    //
    // Phase 2c: fixed Value. Derivation (value computed from another stat) arrives
    // in 2d. ActivityScope is carried as data; no loadout system enforces it yet.
    public class StatModifier
    {
        public readonly StatDefinitionSO TargetStat;
        public readonly string BucketId;      // "flat" / "additive" / "mult" / custom
        public readonly float Value;
        public readonly ActivityScope Scope;
        public ModifierHandle Handle;         // assigned by the container on register

        public StatModifier(StatDefinitionSO targetStat, string bucketId, float value,
                            ActivityScope scope = ActivityScope.InHands)
        {
            TargetStat = targetStat;
            BucketId = bucketId;
            Value = value;
            Scope = scope;
        }
    }
}
