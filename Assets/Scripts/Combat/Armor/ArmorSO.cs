using System.Collections.Generic;
using UnityEngine;
using Combat.Loadout;
using Combat.Perks;

namespace Combat.Armor
{
    // An armour definition: its slot, the modifiers it always grants, and its perks.
    [CreateAssetMenu(fileName = "Armor", menuName = "Combat/Armor/Armor")]
    public class ArmorSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        public string displayName = "";

        public ArmorSlotSO slot;

        [Tooltip("Weapons target applies to every loadout weapon matching the type filter. " +
                 "InHands scope only matters for the Weapons target.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        [Header("Perks")]
        [Tooltip("Always present on every copy.")]
        public List<PerkSO> intrinsicPerks = new List<PerkSO>();
        [Tooltip("One perk is rolled from each column. Empty = use the slot's columns.")]
        public List<PerkColumn> perkColumns = new List<PerkColumn>();
        [Tooltip("Fixed pick per column for curated rolls. Empty entry = random for that column.")]
        public List<PerkSO> curatedRoll = new List<PerkSO>();

        // Own columns, else the slot's.
        public List<PerkColumn> ResolvedPerkColumns
            => perkColumns != null && perkColumns.Count > 0 ? perkColumns
             : slot != null ? slot.perkColumns : perkColumns;
    }
}