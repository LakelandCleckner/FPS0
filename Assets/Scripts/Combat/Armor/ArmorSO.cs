using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Loadout;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Armor
{
    // One stat change an armour piece grants while equipped.
    [Serializable]
    public class ArmorModifierEntry
    {
        public StatDefinitionSO stat;

        [Tooltip("Flat adds to the base. Additive: percent on ScaledBase stats (0.1 = +10%), " +
                 "a straight add on Pool stats. Multiplicative: its own x(1 + value).")]
        public ModifierBucket bucket = ModifierBucket.Flat;

        public float value;

        [Tooltip("Player stats, or the stats of each loadout weapon.")]
        public ModifierTarget target = ModifierTarget.Player;

        [Tooltip("Weapons target only. Empty = every weapon type.")]
        public List<WeaponTypeSO> weaponTypes = new List<WeaponTypeSO>();

        [Tooltip("Weapons target only. InLoadout = every slotted weapon; InHands = only the held one.")]
        public ActivityScope scope = ActivityScope.InLoadout;
    }

    // An armour definition: its slot and the modifiers it grants.
    [CreateAssetMenu(fileName = "Armor", menuName = "Combat/Armor/Armor")]
    public class ArmorSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        public string displayName = "";

        public ArmorSlotSO slot;

        public List<ArmorModifierEntry> modifiers = new List<ArmorModifierEntry>();
    }
}
