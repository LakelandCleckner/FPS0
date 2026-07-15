using System.Collections.Generic;
using Combat.Core;
using Combat.Stats;

namespace Combat.Sources
{
    // Anything that can produce damage: weapon, grenade, melee, explosive barrel.
    // Source-agnostic.
    //
    // Phase 2i-a: exposes the source's own StatContainer, so Source-scoped
    // derivations can resolve ANY of its stats (weapon_damage, rpm, ...), not just
    // the base-damage snapshot.
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

        // The ATTACKER's stat container (the wielder). Nullable — a sourceless
        // hazard has none.
        StatContainer AttackerStats { get; }

        // The SOURCE's OWN stat container (this weapon's stats). Nullable — a simple
        // source (thrown rock) may have none.
        StatContainer SourceStats { get; }
    }
}