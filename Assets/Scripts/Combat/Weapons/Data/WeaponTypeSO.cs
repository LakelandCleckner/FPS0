using UnityEngine;

namespace Combat.Weapons
{
    // Top of the weapon hierarchy: the FAMILY (Sidearm, Auto Rifle, ...).
    // Minimal for now — identity/display + an optional loadout slot category as
    // inert data (no enforcement yet; a future loadout system may read it).
    [CreateAssetMenu(fileName = "WeaponType", menuName = "Combat/Weapons/Weapon Type")]
    public class WeaponTypeSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        [Tooltip("Display name (safe to rename).")]
        public string displayName = "";

        [Header("Loadout (data only — no enforcement yet)")]
        [Tooltip("Optional slot category for a future loadout system. Unused for now.")]
        public string slotCategory = "";
    }
}