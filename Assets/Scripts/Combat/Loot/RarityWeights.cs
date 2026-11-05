using System;
using UnityEngine;
using Combat.Perks;

namespace Combat.Loot
{
    // Odds for an item's rarity. Mythic means "drop a hand-made Mythic"; 0 = never.
    [Serializable]
    public struct RarityWeights
    {
        [Min(0)] public int common;
        [Min(0)] public int uncommon;
        [Min(0)] public int rare;
        [Min(0)] public int legendary;
        [Min(0)] public int mythic;

        public static RarityWeights Default => new RarityWeights { common = 50, uncommon = 30, rare = 14, legendary = 5, mythic = 1 };

        public Rarity Roll() => Roll(common, uncommon, rare, legendary, mythic);

        // Rolls Common to Legendary, or Mythic when a mythic weight is given.
        public static Rarity Roll(int common, int uncommon, int rare, int legendary, int mythic = 0)
        {
            int c = Mathf.Max(0, common), u = Mathf.Max(0, uncommon), r = Mathf.Max(0, rare);
            int l = Mathf.Max(0, legendary), m = Mathf.Max(0, mythic);
            int total = c + u + r + l + m;
            if (total <= 0) return Rarity.Common;

            int roll = UnityEngine.Random.Range(0, total);
            if ((roll -= c) < 0) return Rarity.Common;
            if ((roll -= u) < 0) return Rarity.Uncommon;
            if ((roll -= r) < 0) return Rarity.Rare;
            if ((roll -= l) < 0) return Rarity.Legendary;
            return Rarity.Mythic;
        }
    }
}
