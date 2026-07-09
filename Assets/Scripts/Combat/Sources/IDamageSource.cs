using System.Collections.Generic;
using Combat.Core;

namespace Combat.Sources
{
    // Anything that can START a chain: weapon, grenade, melee, explosive barrel.
    // Produces the data needed to seed the first HitContext. Source-agnostic —
    // GetStats() returns a DamageStats snapshot however the source produces it
    // (a weapon resolves it from a StatContainer; a grenade may set it directly).
    public interface IDamageSource
    {
        DamageStats GetStats();
        List<IHitEffect> GetEffects();
        int Faction { get; }
        DamageTypeSO BaseDamageType { get; }
        int MaxChainDepth { get; }
        float ChainFalloff { get; }
        float ChainGrowth { get; }
        HitDedupMode DedupMode { get; }
    }
}