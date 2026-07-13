using Combat.Core;
using Combat.Sources;

namespace Combat.Status
{
    // One application of a status. Phase 2h: entries are LIVE-LINKED to their source
    // rather than freezing a weight.
    //
    // The tick weight is DERIVED on demand from the source's CURRENT stats:
    //     weight = spec.ComputeRaw(source.GetStats(), target) * ChainMultiplier
    // source.GetStats() is cache-and-version-invalidated (Phase 2g), so this is a
    // cached read — no per-tick recompute unless the source's stats actually changed.
    // If the weapon's damage changes mid-burn, existing stacks reflect it next tick.
    //
    // Per-application identity is preserved: each entry reads ITS OWN source, so a
    // burn from weapon A and one from weapon B scale off their own weapons.
    //
    // ChainMultiplier is frozen at apply (it's a property of THAT application's chain
    // depth, not a live stat). SnapshotFallback is used if the source is gone
    // (destroyed grenade/barrel) so a dead source can't null-crash a live DOT.
    public class StackEntry
    {
        // live link to the source that applied this entry (nullable)
        public IDamageSource Source;

        // fallback stats if Source is null/destroyed (frozen at apply)
        public DamageStats SnapshotFallback;

        // the spec that derives this entry's weight (from the StatusSO)
        public DamageSpec TickSpec;

        // the target (needed by target-anchored derivations)
        public ICombatant Target;

        // frozen at apply: chain scaling for THIS application
        public float ChainMultiplier;

        // metadata
        public DamageTypeSO DamageType;
        public int SourceFaction;
        public int ChainDepth;

        // per-entry lifetime
        public float RemainingDuration;

        // per-entry tick timer (PerEntryTimer mode)
        public float TickAccumulator;

        // LIVE-CACHED weight: derives from the source's current (cached) stats.
        public float Weight
        {
            get
            {
                DamageStats stats = Source != null ? Source.GetStats() : SnapshotFallback;
                return TickSpec.ComputeRaw(stats, Target) * ChainMultiplier;
            }
        }

        public void Set(IDamageSource source, DamageStats snapshotFallback, DamageSpec tickSpec,
                        ICombatant target, float chainMultiplier, DamageTypeSO type,
                        int sourceFaction, int chainDepth, float duration)
        {
            Source = source;
            SnapshotFallback = snapshotFallback;
            TickSpec = tickSpec;
            Target = target;
            ChainMultiplier = chainMultiplier;
            DamageType = type;
            SourceFaction = sourceFaction;
            ChainDepth = chainDepth;
            RemainingDuration = duration;
            TickAccumulator = 0f;
        }

        public void Reset()
        {
            Source = null;
            SnapshotFallback = default;
            TickSpec = default;
            Target = null;
            ChainMultiplier = 1f;
            DamageType = null;
            SourceFaction = 0;
            ChainDepth = 0;
            RemainingDuration = 0f;
            TickAccumulator = 0f;
        }
    }
}