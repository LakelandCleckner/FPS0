using UnityEngine;

namespace Combat.Weapons
{
    // Data description of a firing behavior. The runtime calls CreateBehavior() to
    // build the plain-class IFireBehavior it drives. Behavior-specific params
    // (charge time, burst count) live on the concrete SO subclasses — "conditional
    // stats" that only exist for the behavior that needs them.
    public abstract class FireBehaviorSO : ScriptableObject
    {
        public abstract IFireBehavior CreateBehavior();
    }
}
