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
        ItemLevel   // base value grown by the item scaling curve
    }

    // One stat line rolled on gear. Its value comes from the item's rarity or item level; a greater roll multiplies it.
    [CreateAssetMenu(fileName = "Affix", menuName = "Combat/Affixes/Affix")]
    public class AffixSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        public string displayName = "";

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
        [Tooltip("Item level source: value at item level 1, grown by the item scaling curve.")]
        public float baseValue;

        [Header("Rolls on")]
        public bool onWeapons = true;
        [Tooltip("Empty = every weapon type.")]
        public List<WeaponTypeSO> weaponTypeFilter = new List<WeaponTypeSO>();
        [Tooltip("Empty = every archetype.")]
        public List<WeaponArchetypeSO> archetypeFilter = new List<WeaponArchetypeSO>();
        public bool onArmor = false;
        [Tooltip("Empty = every armor slot.")]
        public List<ArmorSlotSO> slotFilter = new List<ArmorSlotSO>();
        [Tooltip("Relative odds among the affixes an item can roll.")]
        [Min(0f)] public float weight = 1f;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        // Value on an item of this rarity and level, before any greater multiplier.
        public float Value(Rarity rarity, int itemLevel)
        {
            if (valueSource == AffixValueSource.ItemLevel) return baseValue * ItemScaling.Factor(itemLevel);
            if (rarityValues == null || rarityValues.Length == 0) return 0f;
            return rarityValues[Mathf.Clamp((int)rarity, 0, rarityValues.Length - 1)];
        }

        public bool CanRollOn(WeaponSO weapon)
        {
            if (!onWeapons || weapon == null || stat == null) return false;
            var arch = weapon.archetype;
            var type = arch != null ? arch.weaponType : null;
            if (weaponTypeFilter.Count > 0 && (type == null || !weaponTypeFilter.Contains(type))) return false;
            if (archetypeFilter.Count > 0 && (arch == null || !archetypeFilter.Contains(arch))) return false;
            return true;
        }

        public bool CanRollOn(ArmorSO armor)
        {
            if (!onArmor || armor == null || stat == null) return false;
            return slotFilter.Count == 0 || (armor.slot != null && slotFilter.Contains(armor.slot));
        }

        // The modifier this affix grants at a value, for previews and text.
        public ModifierEntry Entry(float value) => new ModifierEntry
        {
            stat = stat, bucket = bucket, value = value, target = target, scope = scope,
            weaponTypes = targetWeaponTypes
        };
    }
}
