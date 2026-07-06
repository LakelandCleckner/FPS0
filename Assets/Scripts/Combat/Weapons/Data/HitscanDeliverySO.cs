using UnityEngine;

namespace Combat.Weapons
{
    // Hitscan delivery description.
    [CreateAssetMenu(fileName = "HitscanDelivery", menuName = "Combat/Weapons/Delivery/Hitscan")]
    public class HitscanDeliverySO : DeliverySO
    {
        [Tooltip("Max hitscan range.")]
        public float range = 100f;

        public override Combat.Delivery.IDelivery CreateDelivery(in DeliveryBuildContext ctx)
            => new Combat.Delivery.HitscanDelivery(ctx.Resolver, range);
    }
}
