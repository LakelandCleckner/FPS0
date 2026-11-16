using UnityEngine;
using Combat.Core;
using Combat.Info;

namespace Combat.Enemies
{
    // A recharging shield layer an enemy modifier can grant.
    [CreateAssetMenu(fileName = "EnemyShield", menuName = "Combat/Enemies/Enemy Shield")]
    public class EnemyShieldSO : ScriptableObject, IDescribable
    {
        [Tooltip("Shield element. Also sets the bar and break colour.")]
        public DamageTypeSO element;
        [Tooltip("Shield size as a fraction of max health.")]
        [Min(0f)] public float percentOfMaxHealth = 0.5f;

        [Header("Damage taken")]
        [Tooltip("Multiplier for damage of the shield's element.")]
        [Min(0f)] public float matchingMultiplier = 2f;
        [Tooltip("Multiplier for every other damage type.")]
        [Min(0f)] public float otherMultiplier = 1f;

        [Header("Recharge")]
        [Tooltip("Seconds without shield damage before it refills. 0 = never refills.")]
        [Min(0f)] public float rechargeDelay = 4f;
        [Tooltip("Seconds to refill from empty.")]
        [Min(0.01f)] public float rechargeTime = 2f;

        [Header("Break")]
        [Tooltip("Fires where the shield breaks, e.g. a blast that hurts nearby enemies.")]
        public EnemyDeathEffectSO breakEffect;
        [Tooltip("Only fire the break effect when the matching element breaks it.")]
        public bool breakEffectOnlyOnMatch = true;
        [Tooltip("Used when there's no element.")]
        public Color fallbackColor = new Color(0.4f, 0.8f, 1f, 1f);

        public Color Color => element != null ? element.color : fallbackColor;

        public void Describe(Describer d)
        {
            if (element != null) d.Element(element).Add(" ");
            d.Keyword("shield", "shield").Add(" worth ").Percent(percentOfMaxHealth, "size").Add(" of max health.");
            if (element != null) d.Next().Add("Takes ").Num("x" + Describer.Trim(matchingMultiplier), "matching").Add(" damage from ").Element(element)
                                         .Add(", ").Num("x" + Describer.Trim(otherMultiplier), "other").Add(" from everything else.");
            if (rechargeDelay > 0f) d.Next().Add("Refills after ").Seconds(rechargeDelay, "delay").Add(" without damage, over ").Seconds(rechargeTime, "refill").Add(".");
            else d.Next().Add("Never refills.");
            if (breakEffect != null)
            {
                d.Next();
                breakEffect.DescribeAs(d, breakEffectOnlyOnMatch && element != null ? "Broken with its element: " : "When broken: ");
            }
        }
    }
}
