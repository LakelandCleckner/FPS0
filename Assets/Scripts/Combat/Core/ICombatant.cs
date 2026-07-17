using Combat.Stats;

namespace Combat.Core
{
    // A combatant: an entity with a stat container and (optionally) health + defense.
    // Used as ATTACKER (whose stats scale a hit; whose health a derivation may read)
    // and as TARGET (who's hit).
    //
    // Implemented by CombatantStats (the identity is the stat-bearer). Stats are read
    // directly from Stats; health is delegated to a CombatantHealth sibling, so
    // reading a stat (e.g. crit) NEVER routes through the health component.
    public interface ICombatant
    {
        // The combatant's stat container — the direct path for stat derivations
        // (crit, global damage, ...). No health involved.
        StatContainer Stats { get; }

        // Health (delegated to a CombatantHealth sibling; 0/defaults if none).
        float MaxHealth { get; }
        float CurrentHealth { get; }
        bool IsDying { get; }

        bool IsDebuffed { get; }

        int Faction { get; }

        // Single composed DEFENSIVE multiplier for an incoming hit.
        float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart);
    }
}