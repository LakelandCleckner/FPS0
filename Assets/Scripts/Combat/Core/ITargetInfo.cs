namespace Combat.Core
{
    // Minimal view of a target that the system needs. Your EnemyHealth would
    // implement / expose this.
    public interface ITargetInfo
    {
        float MaxHealth { get; }
        float CurrentHealth { get; }
        bool IsDying { get; }  // already-dead / despawning guard
        int Faction { get; }   // for can-damage checks
    }
}
