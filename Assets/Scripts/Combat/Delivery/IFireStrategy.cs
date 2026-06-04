using UnityEngine;
using Combat.Sources;

namespace Combat.Delivery
{
    // Delivery layer. Produces a hit, seeds the first HitContext from the
    // passed-in source, hands it to the resolver. Source is a parameter so one
    // strategy instance can serve any source (weapon, grenade, barrel).
    public interface IFireStrategy
    {
        void Fire(Vector3 origin, Vector3 direction, IDamageSource source);
    }
}
