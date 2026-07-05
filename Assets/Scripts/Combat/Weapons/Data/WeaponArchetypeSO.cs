using UnityEngine;

namespace Combat.Weapons
{
    // The frame STAT PROFILE within a type (e.g. a 140 vs 120 sidearm). Owns the
    // BASE numeric stats + crosshair. Shared across many named weapons; a weapon's
    // intrinsic deltas adjust these (see WeaponSO). Damage type is NOT here — it's
    // per-weapon (WeaponSO).
    //
    // Delivery is intentionally NOT here in Phase 1a (fire-mode system is 1b).
    [CreateAssetMenu(fileName = "WeaponArchetype", menuName = "Combat/Weapons/Weapon Archetype")]
    public class WeaponArchetypeSO : ScriptableObject
    {
        [Tooltip("The family this archetype belongs to.")]
        public WeaponTypeSO weaponType;

        [Tooltip("Stable id / display.")]
        public string id = "";
        public string displayName = "";

        [Header("Base Stats (the frame profile)")]
        public float weaponDamage = 0f;
        public float critDamage = 0f;
        public float critChance = 0f;
        public float globalDamageMultiplier = 1f; // neutral multiplier default
        [Tooltip("Fire rate — core to the frame identity.")]
        public float roundsPerMinute = 0f;

        [Header("Presentation")]
        [Tooltip("Crosshair for this frame (certain archetypes need specific ones).")]
        public GameObject crosshairPrefab;
    }
}