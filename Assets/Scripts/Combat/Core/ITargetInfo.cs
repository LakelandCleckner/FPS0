namespace Combat.Core
{   
    // Minimal view of a target the combat system needs. Implemented by
    // EnemyHealth (and any destructible object).

    public interface ITargetInfo
    {
        float MaxHealth { get; }
        float CurrentHealth { get; }
        bool IsDying { get; }  // already-dead / despawning guard
        int Faction { get; }   // for can-damage checks

        // Resistance/weakness by damage type. 1.0 = neutral, <1 = resistant,
        // >1 = weak, 0 = immune. Applied at the damage chokepoint so EVERY
        // damage path (hits, ticks, chains) respects it automatically.
        float GetResistanceMultiplier(DamageTypeSO type);

    }
}
