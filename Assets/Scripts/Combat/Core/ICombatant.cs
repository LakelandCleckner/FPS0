using Combat.Stats;

namespace Combat.Core
{
    // Any entity the combat system can damage or attribute damage to: enemies, the
    // player, destructibles. Used as TARGET (who's hit) and as ATTACKER (whose stats
    // scale the hit) — hence "combatant", not "target".
    //
    // Phase 2i-a: exposes the entity's StatContainer, so derivations can resolve
    // stats scoped to any combatant (attacker or target), and health becomes
    // stat-driven (MaxHealth resolves from the container).
    public interface ICombatant
    {
        // Runtime health state. MaxHealth is STAT-DRIVEN (resolved from Stats);
        // CurrentHealth is authoritative runtime state (TakeDamage writes it).
        float MaxHealth { get; }
        float CurrentHealth { get; }

        bool IsDying { get; }  // already-dead / despawning guard
        int Faction { get; }   // for can-damage checks

        // This combatant's stat container (max_health, damage_taken, crit for a
        // player-combatant, ...). Nullable in principle; a combatant without stats
        // resolves defaults.
        StatContainer Stats { get; }

        // Single composed DEFENSIVE multiplier for an incoming hit. The combatant
        // folds ALL of its damage-reduction/amplification into one number here, so
        // callers apply one multiplier and never need to know which layers exist.
        // 1 = neutral, <1 resist, >1 weak, 0 immune.
        float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart);
    }
}