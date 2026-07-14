namespace Combat.Core
{   
    // Minimal view of a target the combat system needs. Implemented by
    // CombatantHealth (and any destructible object).

    public interface ICombatant
    {
        float MaxHealth { get; }
        float CurrentHealth { get; }
        bool IsDying { get; }  // already-dead / despawning guard
        int Faction { get; }   // for can-damage checks

        // Single composed DEFENSIVE multiplier for an incoming hit.The target
        // folds ALL of its own damage-reduction/amplification systems into one
        // number here — type resistance now, body-part resistance and any future
        // defensive layers later — so callers apply one multiplier and never need
        // to know which defensive systems exist. 1 = neutral, <1 resist, >1 weak,
        // 0 immune.
        float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart);


    }
}
