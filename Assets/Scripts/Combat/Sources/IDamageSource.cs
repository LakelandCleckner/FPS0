using System.Collections.Generic;
using Combat.Core;
using Combat.Stats;

namespace Combat.Sources
{
    // Anything that can START a chain: weapon, grenade, melee, explosive barrel.
    // Source-agnostic. GetStats() returns a DamageStats snapshot; AttackerStats
    // returns the attacker's PLAYER-SCOPE stat container (crit, global) or null for
    // a sourceless source (hazard/environment).
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

        // Attacker's player-scope stats (nullable). Deliveries stamp this onto the
        // hit context as AttackerStats so the resolver can read crit/global.
        StatContainer AttackerStats { get; }
    }
}