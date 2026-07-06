using UnityEngine;

namespace Combat.Weapons
{
    // The frame STAT PROFILE within a type. Owns base numeric stats + crosshair +
    // primary fire mode + ammo profile. Damage type is per-weapon.
    [CreateAssetMenu(fileName = "WeaponArchetype", menuName = "Combat/Weapons/Weapon Archetype")]
    public class WeaponArchetypeSO : ScriptableObject
    {
        [Tooltip("The family this archetype belongs to.")]
        public WeaponTypeSO weaponType;

        public string id = "";
        public string displayName = "";

        [Header("Base Stats (the frame profile)")]
        public float weaponDamage = 0f;
        public float critDamage = 0f;
        public float critChance = 0f;
        public float globalDamageMultiplier = 1f;
        [Tooltip("Fire rate — core to the frame identity.")]
        public float roundsPerMinute = 0f;

        [Header("Ammo / Reload")]
        [Tooltip("Rounds the magazine holds.")]
        public float magazineSize = 10f;
        [Tooltip("Seconds to reload.")]
        public float reloadTime = 1.5f;
        [Tooltip("If true, reserves never deplete (primaries). A weapon can override.")]
        public bool infiniteReserves = false;

        [Header("Fire Mode (primary)")]
        public FireModeSO primaryFireMode;

        [Header("Presentation")]
        public GameObject crosshairPrefab;
    }
}