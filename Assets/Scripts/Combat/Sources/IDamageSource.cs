using System.Collections.Generic;
using Combat.Core;

namespace Combat.Sources
{
    // Anything that can START a chain: weapon, grenade, melee, explosive barrel.
    // Produces the data needed to seed the first HitContext.
    public interface IDamageSource
    {
        StatBlock GetStats();
        List<IHitEffect> GetEffects();
        int Faction { get; }
        DamageType BaseDamageType { get; }
        int MaxChainDepth { get; }
        float ChainFalloff { get; }
        float ChainGrowth { get; }
        HitDedupMode DedupMode { get; }
    }
}
