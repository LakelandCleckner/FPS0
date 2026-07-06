using UnityEngine;

namespace Combat.Weapons
{
    // The frame STAT PROFILE within a type. Owns base numeric stats + crosshair +
    // the PRIMARY FIRE MODE (behavior + delivery). Damage type is per-weapon.
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

        [Header("Fire Mode (primary)")]
        [Tooltip("The primary fire mode: firing behavior + delivery. Alt modes later.")]
        public FireModeSO primaryFireMode;

        [Header("Presentation")]
        [Tooltip("Crosshair for this frame.")]
        public GameObject crosshairPrefab;
    }
}