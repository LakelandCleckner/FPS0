using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Loadout;
using Combat.Perks;
using Combat.Progression;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Affixes
{
    public enum AffixValueSource
    {
        Rarity,     // one value per rarity
        ItemLevel   // level 1 value, ramped up to the library's multiplier at max item level
    }

    // Each group rolls into its own slots, so affixes only compete within their group.
    public enum AffixGroup
    {
        Stat,    // feel stats, value by rarity
        Damage,  // damage scaling, value ramps with item level
        Element, // element affixes on elemental armor (Legendary and up)
        Defense  // survival on armor: health, damage reduction, resistances
    }

    // One stat line rolled on gear. Its value comes from the item's rarity or item level; a greater roll multiplies it.
    [CreateAssetMenu(fileName = "Affix", menuName = "Combat/Affixes/Affix")]
    public class AffixSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        public string displayName = "";
        public AffixGroup group = AffixGroup.Stat;

        [Header("Stat")]
        public StatDefinitionSO stat;
        [Tooltip("Flat adds to the base. Additive: percent on ScaledBase stats (0.1 = +10%), a straight add on Pool stats. Multiplicative: its own x(1 + value).")]
        public ModifierBucket bucket = ModifierBucket.Flat;
        [Tooltip("Player stats, or weapon stats.")]
        public ModifierTarget target = ModifierTarget.Weapons;
        [Tooltip("InLoadout = while slotted; InHands = only while held.")]
        public ActivityScope scope = ActivityScope.InLoadout;
        [Tooltip("Armor affixes with the Weapons target: which weapon types they boost. Empty = every weapon type.")]
        public List<WeaponTypeSO> targetWeaponTypes = new List<WeaponTypeSO>();

        [Header("Value")]
        public AffixValueSource valueSource = AffixValueSource.Rarity;
        [Tooltip("Rarity source: value at Common, Uncommon, Rare, Legendary, Mythic.")]
        public float[] rarityValues = { 0f, 0f, 0f, 0f, 0f };
        [Tooltip("Item level source: value at item level 1, ramped by the library up to max item level.")]
        public float baseValue;

        [Header("Rolls on")]
        public bool onWeapons = true;
        [Tooltip("Rolls on weapons of these types or the archetypes below. Both empty = every weapon.")]
        public List<WeaponTypeSO> weaponTypeFilter = new List<WeaponTypeSO>();
        [Tooltip("Rolls on these archetypes or the types above. Both empty = every weapon.")]
        public List<WeaponArchetypeSO> archetypeFilter = new List<WeaponArchetypeSO>();
        public bool onArmor = false;
        [Tooltip("Rolls on armor in these slots or the pieces below. Both empty = every armor piece.")]
        public List<ArmorSlotSO> slotFilter = new List<ArmorSlotSO>();
        [Tooltip("Rolls on these armor pieces or the slots above. Both empty = every armor piece.")]
        public List<ArmorSO> armorFilter = new List<ArmorSO>();
        [Tooltip("Only rolls on items of this element. Empty = any.")]
        public Combat.Core.DamageTypeSO element;
        [Tooltip("Relative odds among the affixes an item can roll.")]
        [Min(0f)] public float weight = 1f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        // Value on an item of this rarity and level, before any greater multiplier.
        public float Value(Rarity rarity, int itemLevel)
        {
            if (valueSource == AffixValueSource.ItemLevel) return baseValue * AffixLibrary.LevelRamp(itemLevel);
            if (rarityValues == null || rarityValues.Length == 0) return 0f;
            return rarityValues[Mathf.Clamp((int)rarity, 0, rarityValues.Length - 1)];
        }

        public bool CanRollOn(WeaponSO weapon)
            => weapon != null && (element == null || element == weapon.baseDamageType) && CanRollOn(weapon.archetype);

        public bool CanRollOn(WeaponArchetypeSO arch)
        {
            if (!onWeapons || stat == null) return false;
            if (weaponTypeFilter.Count == 0 && archetypeFilter.Count == 0) return true;
            var type = arch != null ? arch.weaponType : null;
            return (type != null && weaponTypeFilter.Contains(type)) || (arch != null && archetypeFilter.Contains(arch));
        }

        public bool CanRollOn(ArmorSO armor)
        {
            // Armor has no element yet, so element affixes stay off armor.
            if (!onArmor || armor == null || stat == null || element != null) return false;
            if (slotFilter.Count == 0 && armorFilter.Count == 0) return true;
            return (armor.slot != null && slotFilter.Contains(armor.slot)) || armorFilter.Contains(armor);
        }

        // The modifier this affix grants at a value, for previews and text.
        public ModifierEntry Entry(float value) => new ModifierEntry
        {
            stat = stat, bucket = bucket, value = value, target = target, scope = scope,
            weaponTypes = targetWeaponTypes
        };
    }
}
