using Combat.Delivery;

namespace Combat.Sources
{
    // Optional capability: a damage source that can supply projectile parameters.
    // A hitscan-only weapon doesn't implement this; a projectile weapon does.
    // Keeps projectile-specific data out of the base IDamageSource contract.
    public interface IProjectileSource
    {
        ProjectileConfig GetProjectileConfig();
    }
}
