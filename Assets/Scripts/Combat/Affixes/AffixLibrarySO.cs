using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.Affixes
{
    // Every affix that can roll, and how many roll and how often they're greater.
    [CreateAssetMenu(fileName = "AffixLibrary", menuName = "Combat/Affixes/Affix Library")]
    public class AffixLibrarySO : ScriptableObject
    {
        public List<AffixSO> affixes = new List<AffixSO>();

        [Header("Rolls")]
        [Tooltip("Affixes per item at Common, Uncommon, Rare, Legendary, Mythic.")]
        public int[] countByRarity = { 2, 2, 2, 2, 2 };
        [Tooltip("Chance each rolled affix is greater.")]
        [Range(0f, 1f)] public float greaterChance = 0.05f;
        [Min(1f)] public float greaterMultiplier = 1.5f;

        private static readonly List<AffixSO> pool = new List<AffixSO>();

        public int CountFor(Rarity rarity)
        {
            if (countByRarity == null || countByRarity.Length == 0) return 0;
            return Mathf.Max(0, countByRarity[Mathf.Clamp((int)rarity, 0, countByRarity.Length - 1)]);
        }

        public void Roll(WeaponSO weapon, Rarity rarity, List<AffixRoll> into) => Roll(a => a.CanRollOn(weapon), rarity, into);
        public void Roll(ArmorSO armor, Rarity rarity, List<AffixRoll> into) => Roll(a => a.CanRollOn(armor), rarity, into);

        // Weighted picks from the eligible affixes, no repeats, each rolling for greater.
        private void Roll(Predicate<AffixSO> eligible, Rarity rarity, List<AffixRoll> into)
        {
            into.Clear();
            pool.Clear();
            foreach (var a in affixes)
                if (a != null && a.weight > 0f && eligible(a) && !pool.Contains(a)) pool.Add(a);

            int count = CountFor(rarity);
            for (int n = 0; n < count && pool.Count > 0; n++)
            {
                float total = 0f;
                foreach (var a in pool) total += a.weight;
                float r = UnityEngine.Random.value * total;
                int pick = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    r -= pool[i].weight;
                    if (r < 0f) { pick = i; break; }
                }
                into.Add(new AffixRoll(pool[pick], UnityEngine.Random.value < greaterChance));
                pool.RemoveAt(pick);
            }
            pool.Clear();
        }
    }
}
