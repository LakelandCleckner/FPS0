using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Affixes
{
    // The active affix library, set by the scene's StatBootstrap. Edit mode reads it from the scene.
    public static class AffixLibrary
    {
        private static AffixLibrarySO active;

        public static AffixLibrarySO Active
        {
            get
            {
                if (active == null)
                {
                    var boot = Object.FindFirstObjectByType<StatBootstrap>();
                    if (boot != null) active = boot.Affixes;
                }
                return active;
            }
            set => active = value;
        }

        public static float GreaterMultiplier => Active != null ? Active.greaterMultiplier : 1.5f;

        // Rolled affixes, or a Mythic's fixed ones.
        public static void RollFor(WeaponSO weapon, Rarity rarity, List<AffixRoll> into)
        {
            into.Clear();
            if (weapon == null) return;
            if (weapon.mythic) { CopyFixed(weapon.fixedAffixes, into); return; }
            if (Active != null) Active.Roll(weapon, rarity, into);
        }

        public static void RollFor(ArmorSO armor, Rarity rarity, List<AffixRoll> into)
        {
            into.Clear();
            if (armor == null) return;
            if (armor.mythic) { CopyFixed(armor.fixedAffixes, into); return; }
            if (Active != null) Active.Roll(armor, rarity, into);
        }

        private static void CopyFixed(List<AffixRoll> fixedAffixes, List<AffixRoll> into)
        {
            if (fixedAffixes == null) return;
            foreach (var f in fixedAffixes)
                if (f != null && f.affix != null) into.Add(new AffixRoll(f.affix, f.greater));
        }
    }
}
