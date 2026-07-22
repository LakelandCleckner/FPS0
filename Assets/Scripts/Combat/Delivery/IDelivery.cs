using UnityEngine;
using Combat.Core;
using Combat.Sources;

namespace Combat.Delivery
{
    // The DELIVERY axis: HOW a shot reaches the target (hitscan, projectile).
    // Converted from a scene MonoBehaviour to a plain class so it can be built from
    // data (a DeliverySO) with the resolver injected at construction, cached and
    // reused. Executes one shot when Fire is called by the runtime — it knows nothing
    // about firing behavior.
    //
    // (Replaces the old IFireStrategy MonoBehaviour interface.)
    //
    // Fire rework: takes the ShotInfo the behaviour produced and stamps it onto the
    // HitContext, so shot identity, burst index and charge level reach effects and perks.
    public interface IDelivery
    {
        void Fire(Vector3 origin, Vector3 direction, IDamageSource source, in ShotInfo shot);
    }
}