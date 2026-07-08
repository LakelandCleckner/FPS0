namespace Combat.Stats
{
    // A modifier's CONTRIBUTION for resolution purposes: which bucket it feeds, and
    // its value. Phase 2b uses this minimal form to prove the bucket math. The full
    // modifier (source handles, activity scopes, derivation) arrives in 2c/2d and
    // will produce values of this shape at resolve time.
    public readonly struct StatModifierValue
    {
        public readonly string BucketId;  // which bucket this feeds
        public readonly float Value;       // signed; negative = reduction

        public StatModifierValue(string bucketId, float value)
        {
            BucketId = bucketId;
            Value = value;
        }
    }
}
