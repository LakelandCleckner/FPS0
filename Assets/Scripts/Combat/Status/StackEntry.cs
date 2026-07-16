using Combat.Core;
using Combat.Sources;

namespace Combat.Status
{
    // One application of a status. Phase 2i-b: weight derives LIVE through a
    // DerivationContext built from the entry's stored refs (source / attacker /
    // target), so a tick can scale off any participant's stats or health quantities
    // — resolved cached (source.SourceStats / attacker.Stats are version-invalidated).
    //
    // DEAD-SOURCE FALLBACK: if the source is destroyed (Source becomes null), the
    // entry freezes at the last weight it successfully computed while alive, so a
    // live DOT from a now-gone grenade keeps ticking sanely. (Replaces the old
    // DamageStats snapshot — the fallback is the cached OUTPUT, not a frozen input,
    // so it works for any derivation, not just base damage.)
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

        // LIVE weight: resolve through the derivation context. If the source is gone
        // AND the derivation needs it, fall back to the last alive weight.
        public float Weight
        {
            get
            {
                // Source destroyed (Unity-null) -> freeze at last known weight
                bool sourceAlive = !(Source is UnityEngine.Object o) || o != null;
                if (!sourceAlive)
                    return hasLastWeight ? lastAliveWeight : 0f;

                var dctx = new DerivationContext(Attacker, Source, Target);
                float w = TickSpec.Resolve(in dctx) * ChainMultiplier;

                lastAliveWeight = w;
                hasLastWeight = true;
                return w;
            }
        }

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
        }
    }
}