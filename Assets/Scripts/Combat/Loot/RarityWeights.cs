using System;
using UnityEngine;
using Combat.Perks;

namespace Combat.Loot
{
    // Odds for an item's rarity. Mythic never rolls; it only comes from Mythic definitions.
    [Serializable]
    public struct RarityWeights
    {
        [Min(0)] public int common;
        [Min(0)] public int rare;
        [Min(0)] public int legendary;

        public static RarityWeights Default => new RarityWeights { common = 60, rare = 30, legendary = 10 };

        public Rarity Roll() => Roll(common, rare, legendary);

        public static Rarity Roll(int common, int rare, int legendary)
        {
            int total = Mathf.Max(0, common) + Mathf.Max(0, rare) + Mathf.Max(0, legendary);
            if (total <= 0) return Rarity.Common;
            int r = UnityEngine.Random.Range(0, total);
            if (r < common) return Rarity.Common;
            return r < common + rare ? Rarity.Rare : Rarity.Legendary;
        }
    }
}
