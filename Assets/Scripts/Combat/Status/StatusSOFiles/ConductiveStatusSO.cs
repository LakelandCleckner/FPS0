using UnityEngine;
using Combat.Info;

namespace Combat.Status
{
    // While active, direct hits on the target arc part of their damage to nearby enemies.
    [CreateAssetMenu(fileName = "ConductiveStatus", menuName = "Combat/Status/Conductive")]
    public class ConductiveStatusSO : StatusSO
    {
        [Header("Arc")]
        [Tooltip("Share of each direct hit's damage passed to every arc target.")]
        [Range(0f, 2f)] public float arcFraction = 0.3f;
        [Min(1)] public int arcTargets = 2;
        [Tooltip("Metres from the struck target.")]
        [Min(0.5f)] public float arcRadius = 8f;
        public LayerMask enemyLayers = 1 << 7;

        public override void Describe(Describer d)
        {
            d.Add("Direct hits on this target arc ").Percent(arcFraction, "arc").Add(" of their damage to up to ")
             .Count(arcTargets, "targets").Add(arcTargets == 1 ? " enemy" : " enemies").Add(" within ").Meters(arcRadius, "radius");
            if (damageType != null) d.Add(" as ").Element(damageType);
            d.Add(".").Next().Add("Lasts ").Seconds(duration, "duration").Add(".");
        }
    }
}
