using UnityEngine;

namespace Combat.Progression
{
    // Item level growth: Factor(level) = (1 + growth)^(level - 1), so level 1 is x1.
    [CreateAssetMenu(fileName = "ItemScaling", menuName = "Combat/Progression/Item Scaling")]
    public class ItemScalingSO : ScriptableObject
    {
        [Tooltip("Growth per item level. 0.08 = +8% per level.")]
        [Min(0f)] public float growth = 0.08f;
        [Tooltip("Highest item level.")]
        [Min(1)] public int maxLevel = 100;

        public int Clamp(int level) => Mathf.Clamp(level, 1, maxLevel);
        public float Factor(int level) => Mathf.Pow(1f + growth, Clamp(level) - 1);
    }
}
