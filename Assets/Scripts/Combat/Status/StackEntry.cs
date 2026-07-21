using Combat.Core;
using Combat.Sources;

namespace Combat.Status
{
    // One application of a status. Weight derives LIVE through a DerivationContext
    // built from the entry's stored refs (source / attacker / target), so a tick can
    // scale off any participant's stats or health quantities.
    //
    // DEAD-SOURCE FALLBACK: if the source is destroyed (Source becomes null), the
    // entry freezes at the last weight it successfully computed while alive, so a
    // live DOT from a now-gone grenade keeps ticking sanely. The fallback caches the
    // OUTPUT, not a frozen input, so it works for any derivation.
    //
    // PERF: Weight is now cached PER FRAME. It used to rebuild a DerivationContext
    // and re-resolve the whole spec on every read — and it is read several times per
    // tick (guard, log, argument) plus once per entry by SummedWeight and EvictOne.
    // Semantics change from "live per read" to "live per frame"; nothing observes it
    // more than once per frame, and a modifier applied this frame still lands this
    // frame unless it lands strictly between two reads within one frame.
    public class StackEntry
    {
        public IDamageSource Source;    // live link (nullable once destroyed)
        public ICombatant Attacker;     // for Attacker-scope derivations (nullable)
        public ICombatant Target;

        public DamageSpec TickSpec;
        public float ChainMultiplier;   // frozen at apply (this application's chain link)

        public DamageTypeSO DamageType;
        public int SourceFaction;
        public int ChainDepth;

        public float RemainingDuration;
        public float TickAccumulator;

        // last weight computed while the source was alive — the dead-source fallback
        private float lastAliveWeight;
        private bool hasLastWeight;

        // per-frame resolve cache
        private int cachedFrame = -1;
        private float cachedWeight;

        // LIVE weight, resolved at most once per frame. If the source is gone, falls
        // back to the last weight computed while it was alive.
        public float Weight
        {
            get
            {
                int frame = UnityEngine.Time.frameCount;
                if (cachedFrame == frame) return cachedWeight;

                float w;

                // Source destroyed (Unity-null) -> freeze at last known weight
                bool sourceAlive = !(Source is UnityEngine.Object o) || o != null;
                if (!sourceAlive)
                {
                    w = hasLastWeight ? lastAliveWeight : 0f;
                }
                else
                {
                    var dctx = new DerivationContext(Attacker, Source, Target);
                    w = TickSpec.Resolve(in dctx) * ChainMultiplier;

                    lastAliveWeight = w;
                    hasLastWeight = true;
                }

                cachedFrame = frame;
                cachedWeight = w;
                return w;
            }
        }

        // Force the next Weight read to re-resolve. Call if something needs a
        // mid-frame recompute (nothing does today).
        public void InvalidateWeight() => cachedFrame = -1;

        public void Set(IDamageSource source, ICombatant attacker, ICombatant target,
                        DamageSpec tickSpec, float chainMultiplier, DamageTypeSO type,
                        int sourceFaction, int chainDepth, float duration)
        {
            Source = source;
            Attacker = attacker;
            Target = target;
            TickSpec = tickSpec;
            ChainMultiplier = chainMultiplier;
            DamageType = type;
            SourceFaction = sourceFaction;
            ChainDepth = chainDepth;
            RemainingDuration = duration;
            TickAccumulator = 0f;
            lastAliveWeight = 0f;
            hasLastWeight = false;
            cachedFrame = -1;
            cachedWeight = 0f;
        }

        public void Reset()
        {
            Source = null;
            Attacker = null;
            Target = null;
            TickSpec = default;
            ChainMultiplier = 1f;
            DamageType = null;
            SourceFaction = 0;
            ChainDepth = 0;
            RemainingDuration = 0f;
            TickAccumulator = 0f;
            lastAliveWeight = 0f;
            hasLastWeight = false;
            cachedFrame = -1;
            cachedWeight = 0f;
        }
    }
}