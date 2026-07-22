using UnityEngine;
using Combat.Delivery;

namespace Combat.Weapons
{
    // Projectile delivery description. References a ProjectileSO for the prefab and
    // config; the runtime injects only the genuine scene refs (resolver, muzzle).
    //
    // Previously held ProjectileConfig itself and took the prefab from the build
    // context — i.e. from a field on the weapon controller — which is what made every
    // weapon fire the same projectile.
    [CreateAssetMenu(fileName = "ProjectileDelivery", menuName = "Combat/Weapons/Delivery/Projectile")]
    public class ProjectileDeliverySO : DeliverySO
    {
        [Tooltip("Which projectile type this delivery launches.")]
        public ProjectileSO projectile;

        public override IDelivery CreateDelivery(in DeliveryBuildContext ctx)
        {
            if (projectile == null || projectile.prefab == null)
            {
                Debug.LogError($"[{name}] ProjectileDeliverySO has no projectile/prefab assigned.");
                return null;
            }
            return new ProjectileDelivery(ctx.Resolver, projectile.prefab, ctx.Muzzle, projectile.config);
        }
    }
}