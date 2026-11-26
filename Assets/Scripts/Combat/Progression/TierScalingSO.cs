using UnityEngine;
using Combat.Stats;

namespace Combat.Progression
{
    // Enemy growth per tier: Factor(tier) = (1 + growth)^(tier - 1), so tier 1 is x1.
    [CreateAssetMenu(fileName = "TierScaling", menuName = "Combat/Progression/Tier Scaling")]
    public class TierScalingSO : ScriptableObject
    {
        [Tooltip("Enemy max health growth per tier. 0.08 = +8% per tier.")]
        [Min(0f)] public float healthGrowth = 0.08f;
        [Tooltip("Enemy damage growth per tier. 0.08 = +8% per tier.")]
        [Min(0f)] public float damageGrowth = 0.08f;

        [Tooltip("Enemy max health stat, multiplied by the health factor.")]
        public StatDefinitionSO healthStat;
        [Tooltip("Enemy stat set to the damage factor; the enemy damage formula multiplies by it.")]
        public StatDefinitionSO damageStat;

        public float HealthFactor(int tier) => Mathf.Pow(1f + healthGrowth, Mathf.Max(1, tier) - 1);
        public float DamageFactor(int tier) => Mathf.Pow(1f + damageGrowth, Mathf.Max(1, tier) - 1);
    }
}
