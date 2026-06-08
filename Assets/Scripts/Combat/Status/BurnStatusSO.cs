using UnityEngine;

namespace Combat.Status
{
    // First concrete status. Nothing burn-specific yet beyond identity — the
    // shared StatusSO config already describes a fire DOT. Subclass exists so
    // burn-specific behaviour (and the Fire category in stage 2) has a home.
    [CreateAssetMenu(fileName = "BurnStatus", menuName = "Combat/Status/Burn")]
    public class BurnStatusSO : StatusSO { }
}
