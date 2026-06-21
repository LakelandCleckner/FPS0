using Combat.Core;

namespace Combat.Status
{
    // One application of a status, captured as a snapshot at apply-time. The
    // pool holds a collection of these; a tick sums their weights for its
    // magnitude. Per-application (not per-instance) so each bullet/source that
    // applied the status is its own entry — which is what lets the cap evict the
    // weakest individual entry, and lets each entry expire on its own timer.
    //
    // Plain C# object, pooling-friendly (Reset wipes it for reuse later).
    public class StackEntry
    {
        // damage contribution snapshotted at application (already chain-scaled if
        // it came from a chain link). This is what a tick sums.
        public float Weight;

        // metadata — carried for resistance / intrinsic modifiers, NOT a key
        public DamageTypeSO DamageType;

        // source / chain accounting (for chain reactions later; does not fragment
        // the pool — grouping is by StatusSO, not by source)
        public int SourceFaction;
        public int ChainDepth;

        // per-entry lifetime (default duration model = each entry expires on its
        // own timer from when it landed)
        public float RemainingDuration;

        public void Set(float weight, DamageTypeSO type, int sourceFaction,
                        int chainDepth, float duration)
        {
            Weight = weight;
            DamageType = type;
            SourceFaction = sourceFaction;
            ChainDepth = chainDepth;
            RemainingDuration = duration;
        }

        public void Reset()
        {
            Weight = 0f;
            DamageType = null;
            SourceFaction = 0;
            ChainDepth = 0;
            RemainingDuration = 0f;
        }
    }
}
