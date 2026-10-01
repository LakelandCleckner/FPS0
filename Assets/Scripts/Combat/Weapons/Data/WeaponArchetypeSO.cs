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

        [Header("Aim")]
        [Tooltip("Seconds from hip to fully aimed at ZERO handling. Reduced by handling " +
                 "the same way equip and stow are.")]
        public float baseAdsTime = 0.3f;
        [Tooltip("ADS magnification applied to the player's hip FOV (1.25 = 1.25x). " +
                 "A per-weapon delta layers on top.")]
        public float adsZoom = 1.15f;
        [Tooltip("Shape of the zoom in/out over ads_time.")]
        public AnimationCurve adsCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Un-aim duration as a fraction of ads_time.")]
        public float adsExitFraction = 1f;
        [Tooltip("move_speed multiplier while aiming.")]
        public float adsMoveSpeedMultiplier = 0.8f;

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
         "Reduces equip, stow and ADS time proportionally.")]
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