namespace Combat.Core
{
    public interface IPrecisionSource
    {
        // Damage multiplier on precision (head) hits, before the hitbox's own multiplier
        float PrecisionMultiplier { get; }
    }
}
