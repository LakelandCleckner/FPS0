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
        [Tooltip("Stat affixes per item at Common, Uncommon, Rare, Legendary, Mythic.")]
        public int[] countByRarity = { 2, 2, 2, 2, 2 };
        [Tooltip("Damage affixes per item at Common, Uncommon, Rare, Legendary, Mythic.")]
        public int[] damageCountByRarity = { 2, 3, 4, 5, 6 };
        [Tooltip("Element affixes per item at Common, Uncommon, Rare, Legendary, Mythic. Only elemental armor can roll them.")]
        public int[] elementCountByRarity = { 0, 0, 0, 1, 1 };
        [Tooltip("Defense affixes per item at Common, Uncommon, Rare, Legendary, Mythic. Only armor rolls them.")]
        public int[] defenseCountByRarity = { 1, 1, 2, 2, 3 };
        [Tooltip("Item level affixes at max item level are this many times their level 1 value, rising evenly.")]
        [Min(1f)] public float levelRampAtMax = 10f;
        [Tooltip("Chance each rolled affix is greater.")]
        [Range(0f, 1f)] public float greaterChance = 0.05f;
        [Min(1f)] public float greaterMultiplier = 1.5f;

        private static readonly List<AffixSO> pool = new List<AffixSO>();

        public int CountFor(AffixGroup group, Rarity rarity)
        {
            var counts = group == AffixGroup.Damage ? damageCountByRarity
                       : group == AffixGroup.Element ? elementCountByRarity
                       : group == AffixGroup.Defense ? defenseCountByRarity
                       : countByRarity;
            if (counts == null || counts.Length == 0) return 0;
            return Mathf.Max(0, counts[Mathf.Clamp((int)rarity, 0, counts.Length - 1)]);
        }

        public void Roll(WeaponSO weapon, Rarity rarity, List<AffixRoll> into) => Roll(a => a.CanRollOn(weapon), rarity, into);
        public void Roll(ArmorSO armor, Rarity rarity, List<AffixRoll> into) => Roll(a => a.CanRollOn(armor), rarity, into);

        // Stat, defense, damage, then element affixes, each group filling its own slots.
        private void Roll(Predicate<AffixSO> eligible, Rarity rarity, List<AffixRoll> into)
        {
            into.Clear();
            RollGroup(AffixGroup.Stat, eligible, rarity, into);
            RollGroup(AffixGroup.Defense, eligible, rarity, into);
            RollGroup(AffixGroup.Damage, eligible, rarity, into);
            RollGroup(AffixGroup.Element, eligible, rarity, into);
        }

        // Weighted picks from the group's eligible affixes, no repeats, each rolling for greater.
        private void RollGroup(AffixGroup group, Predicate<AffixSO> eligible, Rarity rarity, List<AffixRoll> into)
        {
            pool.Clear();
            foreach (var a in affixes)
                if (a != null && a.group == group && a.weight > 0f && eligible(a) && !pool.Contains(a)) pool.Add(a);

            int count = CountFor(group, rarity);
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
