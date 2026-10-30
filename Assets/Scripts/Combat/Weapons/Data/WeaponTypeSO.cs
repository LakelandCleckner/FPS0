using System.Collections.Generic;
using UnityEngine;
using Combat.Perks;

namespace Combat.Weapons
{
    // Top of the weapon hierarchy: the family (Sidearm, Auto Rifle, ...).
    [CreateAssetMenu(fileName = "WeaponType", menuName = "Combat/Weapons/Weapon Type")]
    public class WeaponTypeSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        [Tooltip("Display name (safe to rename).")]
        public string displayName = "";

        [Header("Loadout")]
        [Tooltip("Slots weapons of this type may be equipped in. A weapon can override this.")]
        public LoadoutSlotMask allowedSlots = LoadoutSlotMask.All;

        [Header("Perks")]
        [Tooltip("Default roll columns for weapons of this type. A weapon with its own columns uses those instead.")]
        public List<PerkColumn> perkColumns = new List<PerkColumn>();
    }
}