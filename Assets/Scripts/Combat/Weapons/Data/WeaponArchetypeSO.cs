using UnityEngine;

namespace Combat.Weapons
{
    public enum ReloadStyle { Magazine, PerShell }
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

        [Tooltip("Fire rate in ROUNDS per minute — the same meaning for every fire " +
                 "behaviour. A burst weapon at 390 fires 390 rounds per minute; its " +
                 "burst-to-burst pause is derived from this and the burst size, so " +
                 "the number is directly comparable across weapons.")]
        public float roundsPerMinute = 0f;

        [Header("Reload Style")]
        public ReloadStyle reloadStyle = ReloadStyle.Magazine;
        [Tooltip("PerShell: share of reload_time spent opening the reload.")]
        [Range(0f, 1f)] public float shellStartFraction = 0.15f;
        [Tooltip("PerShell: share of reload_time spent closing it (skipped on interrupt).")]
        [Range(0f, 1f)] public float shellEndFraction = 0.2f;
        [Tooltip("PerShell: ready time after a fire-interrupt, as a fraction of equip_time (handling-driven).")]
        public float interruptReadyFraction = 0.3f;
        
        [Header("Ammo / Reload")]
        [Tooltip("Rounds the magazine holds.")]
        public float magazineSize = 10f;

        [Tooltip("Seconds to reload.")]
        public float reloadTime = 1.5f;

        [Tooltip("If true, reserves never deplete (primaries). A weapon can override.")]
        public bool infiniteReserves = false;

        [Header("Handling")]
        [Tooltip("Base handling for this frame. A per-weapon delta layers on top. " +
         "Reduces equip and stow time proportionally, and later ADS time.")]
        public float handling = 0f;

        [Tooltip("Seconds to bring this weapon up at ZERO handling. Frame identity — a " +
                 "rocket launcher is slower than a sidearm no matter how it rolls.")]
        public float baseEquipTime = 0.5f;

        [Tooltip("Seconds to put this weapon away at ZERO handling.")]
        public float baseStowTime = 0.4f;


        // Inline rather than a FireModeSO reference — see FireMode.cs for why.
        [Header("Fire Mode (primary)")]
        public FireMode primaryFire = new FireMode();

        [Header("Presentation")]
        public GameObject crosshairPrefab;
    }
}