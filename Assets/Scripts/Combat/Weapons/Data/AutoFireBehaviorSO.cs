using UnityEngine;

namespace Combat.Weapons
{
    // Full-auto / semi-auto style firing gated by RPM (the current M9 behavior).
    // fireWhileHeld = true  -> full-auto (fires repeatedly while held)
    // fireWhileHeld = false -> semi-auto (one shot per trigger press)
    // Rate comes from the resolved RPM stat, NOT a field here (RPM is an archetype
    // stat). No behavior-specific conditional stats for this one.
    [CreateAssetMenu(fileName = "AutoFireBehavior", menuName = "Combat/Weapons/Fire Behavior/Auto")]
    public class AutoFireBehaviorSO : FireBehaviorSO
    {
        [Tooltip("True = full-auto (fires while held). False = semi-auto (one per press).")]
        public bool fireWhileHeld = true;

        public override IFireBehavior CreateBehavior() => new AutoFireBehavior(fireWhileHeld);
    }
}
