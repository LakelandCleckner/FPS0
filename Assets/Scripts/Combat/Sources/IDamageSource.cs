using System.Collections.Generic;
using Combat.Core;
using Combat.Stats;

namespace Combat.Sources
{
    // Anything that can produce damage: weapon, grenade, melee, explosive barrel.
    // Phase 2i-b: DamageStats/GetStats retired — base damage now derives via
    // PercentOfStat(weapon_damage, Source) resolving from SourceStats live. Attacker
    // is carried as an ICombatant (stats + health), not a bare container.
    public interface IDamageSource
    {
        List<IHitEffect> GetEffects();
        int Faction { get; }
        DamageTypeSO BaseDamageType { get; }
        int MaxChainDepth { get; }
        float ChainFalloff { get; }
        float ChainGrowth { get; }
        HitDedupMode DedupMode { get; }

        // The attacker wielding/owning this source (nullable). Gives both stats
        // (Attacker.Stats) and health (for attacker-scope quantity derivations).
        ICombatant Attacker { get; }

        // This source's OWN stat container (weapon stats). Nullable.
        StatContainer SourceStats { get; }
    }
}