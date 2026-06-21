using Combat.Core;

namespace Combat.Status
{
    // One application of a status, captured as a snapshot at apply-time. The
    // pool holds a collection of these; a tick sums their weights for its
    // magnitude (SharedPoolTimer mode) or each entry ticks on its own timer
    // (PerEntryTimer mode). Per-application so each bullet/source is its own
    // entry — lets the cap evict the weakest individual entry and lets each
    // entry expire (and optionally tick) on its own timer.
    //
    // Plain C# object, pooling-friendly (Reset wipes it for reuse later).
    public class StackEntry
    {
        // damage contribution snapshotted at application (already chain-scaled if
        // it came from a chain link). This is what a tick uses/sums.
        public float Weight;

        // metadata — carried for resistance / intrinsic modifiers, NOT a key
        public DamageTypeSO DamageType;

        // source / chain accounting (for chain reactions later)
        public int SourceFaction;
        public int ChainDepth;

        // per-entry lifetime (default duration model)
        public float RemainingDuration;

        // per-entry tick timer — only used in PerEntryTimer tick-timer mode
        public float TickAccumulator;

        public void Set(float weight, DamageTypeSO type, int sourceFaction,
                        int chainDepth, float duration)
        {
            Weight = weight;
            DamageType = type;
            SourceFaction = sourceFaction;
            ChainDepth = chainDepth;
            RemainingDuration = duration;
            TickAccumulator = 0f;
        }

        public void Reset()
        {
            Weight = 0f;
            DamageType = null;
            SourceFaction = 0;
            ChainDepth = 0;
            RemainingDuration = 0f;
            TickAccumulator = 0f;
        }
    }
}