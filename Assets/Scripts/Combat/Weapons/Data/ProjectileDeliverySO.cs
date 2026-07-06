using UnityEngine;
using Combat.Delivery;

namespace Combat.Weapons
{
    // Projectile delivery description. Holds the ProjectileConfig; the runtime
    // injects the muzzle + prefab (scene refs).
    [CreateAssetMenu(fileName = "ProjectileDelivery", menuName = "Combat/Weapons/Delivery/Projectile")]
    public class ProjectileDeliverySO : DeliverySO
    {
        [Tooltip("Projectile movement/impact params.")]
        public ProjectileConfig projectileConfig = new ProjectileConfig();

        public override IDelivery CreateDelivery(in DeliveryBuildContext ctx)
            => new ProjectileDelivery(ctx.Resolver, ctx.ProjectilePrefab, ctx.Muzzle, projectileConfig);
    }
}
