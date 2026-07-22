using UnityEngine;
using Combat.Core;
using Combat.Delivery;

namespace Combat.Weapons
{
    // Data description of a delivery. The runtime calls CreateDelivery(), injecting
    // the scene refs the delivery needs. Delivery-specific config (range, projectile
    // type) lives on the concrete SO subclasses.
    public abstract class DeliverySO : ScriptableObject
    {
        // ctx carries the scene refs a delivery can't serialize itself.
        public abstract IDelivery CreateDelivery(in DeliveryBuildContext ctx);
    }

    // Scene refs injected at build time (a delivery is a plain class, so it can't
    // hold these as serialized fields — the runtime supplies them).
    //
    // ProjectilePrefab is GONE. It was a scene field standing in for data, which
    // forced every weapon on a controller to fire the same projectile. Prefab and
    // config now live on ProjectileSO, referenced by ProjectileDeliverySO. What
    // remains here is genuinely scene-bound and can't be authored on an asset.
    public readonly struct DeliveryBuildContext
    {
        public readonly WeaponHitResolver Resolver;
        public readonly Transform Muzzle;   // projectile spawn (optional)

        public DeliveryBuildContext(WeaponHitResolver resolver, Transform muzzle)
        {
            Resolver = resolver;
            Muzzle = muzzle;
        }
    }
}