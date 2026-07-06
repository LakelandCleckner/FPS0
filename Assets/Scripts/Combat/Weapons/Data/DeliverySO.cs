using UnityEngine;
using Combat.Core;
using Combat.Delivery;

namespace Combat.Weapons
{
    // Data description of a delivery. The runtime calls CreateDelivery(), injecting
    // the scene refs the delivery needs (resolver, and for projectiles a muzzle +
    // prefab). Delivery-specific config (range, projectile params) lives on the
    // concrete SO subclasses.
    public abstract class DeliverySO : ScriptableObject
    {
        // ctx carries the scene refs a delivery can't serialize itself.
        public abstract IDelivery CreateDelivery(in DeliveryBuildContext ctx);
    }

    // Scene refs injected at build time (a delivery is a plain class, so it can't
    // hold these as serialized fields — the runtime supplies them).
    public readonly struct DeliveryBuildContext
    {
        public readonly WeaponHitResolver Resolver;
        public readonly Transform Muzzle;         // projectile spawn (optional)
        public readonly Projectile ProjectilePrefab; // (optional)

        public DeliveryBuildContext(WeaponHitResolver resolver, Transform muzzle, Projectile projectilePrefab)
        {
            Resolver = resolver;
            Muzzle = muzzle;
            ProjectilePrefab = projectilePrefab;
        }
    }
}
