using UnityEngine;

namespace Combat.Status
{
    // Movement-slow status. No tick damage — it applies a negative multiplicative
    // modifier to Move Speed while active (configured via StatusSO's modifier fields).
    // Subclass exists to give it a Create menu entry and a home for any slow-specific
    // behaviour later.
    [CreateAssetMenu(fileName = "SlowStatus", menuName = "Combat/Status/Slow")]
    public class SlowStatusSO : StatusSO { }
}