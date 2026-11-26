using System;
using UnityEngine;
using Combat.Perks;

namespace Combat.Affixes
{
    // One affix on an item, or one fixed affix on a definition.
    [Serializable]
    public class AffixRoll
    {
        public AffixSO affix;
        [Tooltip("Greater affixes multiply their value by the library's greater multiplier.")]
        public bool greater;

        public AffixRoll() { }
        public AffixRoll(AffixSO affix, bool greater) { this.affix = affix; this.greater = greater; }

        // Value on an item of this rarity and level, greater multiplier included.
        public float Value(Rarity rarity, int itemLevel)
        {
            if (affix == null) return 0f;
            float v = affix.Value(rarity, itemLevel);
            return greater ? v * AffixLibrary.GreaterMultiplier : v;
        }
    }
}
