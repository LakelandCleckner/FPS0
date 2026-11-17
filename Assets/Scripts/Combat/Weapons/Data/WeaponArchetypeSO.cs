using System.Collections.Generic;
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

        [Header("Range Falloff")]
        [Tooltip("Base range stat, 0-100. A per-weapon delta layers on top.")]
        [Range(0f, 100f)] public float range = 50f;
        [Tooltip("ADS falloff start distance (m) at 0 range.")]
        public float falloffStartAtMinRange = 10f;
        [Tooltip("ADS falloff start distance (m) at 100 range. Equal to min for a fixed band.")]
        public float falloffStartAtMaxRange = 20f;
        [Tooltip("Falloff end = start x this. Higher = more gradual drop.")]
        [Min(1f)] public float falloffEndMultiplier = 1.6f;
        [Tooltip("Damage multiplier at and beyond falloff end.")]
        [Range(0f, 1f)] public float minDamagePercent = 0.5f;
        [Tooltip("Hip-fire distance multiplier vs ADS. Can exceed 1.")]
        [Min(0f)] public float hipFalloffMultiplier = 0.85f;
        [Tooltip("Optional drop shape over start->end: x 0..1, y 1 (full) to 0 (floor). Empty = linear.")]
        public AnimationCurve falloffCurve = new AnimationCurve();

        [Header("Aim Assist (bullet magnetism)")]
        [Tooltip("Base aim assist stat, 0-100. A per-weapon delta layers on top.")]
        [Range(0f, 100f)] public float aimAssist = 50f;
        [Tooltip("ADS magnetism cone half-angle (deg) at 0 aim assist.")]
        public float assistAngleAtMin = 0.5f;
        [Tooltip("ADS magnetism cone half-angle (deg) at 100 aim assist.")]
        public float assistAngleAtMax = 1.5f;
        [Tooltip("Hip-fire cone multiplier vs ADS. Can exceed 1.")]
        [Min(0f)] public float assistHipMultiplier = 0.8f;
        [Tooltip("Max magnetism distance = falloff end x this.")]
        [Min(0f)] public float assistDistanceMultiplier = 1f;

        [Header("Recoil")]
        [Tooltip("Base stability stat, 0-100. Scales each kick and the viewmodel bounce. A per-weapon delta layers on top.")]
        [Range(0f, 100f)] public float stability = 50f;
        [Tooltip("Base recoil direction stat, 0-100. Sets the sideways bias and how much the pattern wanders (100 = straight up).")]
        [Range(0f, 100f)] public float recoilDirection = 75f;
        [Tooltip("Upward kick per shot (deg) at 0 stability.")]
        public float kickAtMinStability = 1.0f;
        [Tooltip("Upward kick per shot (deg) at 100 stability.")]
        public float kickAtMaxStability = 0.3f;
        [Tooltip("Sideways kick per degree of upward kick at full bounce intensity (0 recoil direction).")]
        public float sideKickScale = 0.7f;
        [Tooltip("Random share on top of the fixed pattern. 0 = identical sprays, 1 = mostly random.")]
        [Range(0f, 1f)] public float patternVariance = 0.15f;
        [Tooltip("Kick multiplier on the first shot of a spray.")]
        [Min(0f)] public float firstShotMultiplier = 1f;
        [Tooltip("Kick multiplier by shot number in the spray (x = shot index, y = multiplier).")]
        public AnimationCurve kickRamp = AnimationCurve.Linear(0f, 1f, 12f, 1.25f);
        [Tooltip("Kick multiplier while fully aimed.")]
        [Range(0f, 1f)] public float adsRecoilMultiplier = 0.85f;
        [Tooltip("Seconds each kick takes to land, so it reads as a push rather than a snap.")]
        [Min(0.001f)] public float kickTime = 0.035f;
        [Tooltip("Seconds after a shot before the aim starts recentring.")]
        [Min(0f)] public float centreDelay = 0.06f;
        [Tooltip("Recentring speed (1/s). Higher returns faster. Not affected by stability.")]
        [Min(0f)] public float centreSpeed = 10f;
        [Tooltip("Share of the uncountered climb that recentres. 1 = back to the start point.")]
        [Range(0f, 1f)] public float recenterPercent = 1f;
        [Tooltip("Random miss (deg) per shot left after recentring.")]
        [Min(0f)] public float residualError = 0.04f;
        [Tooltip("Authored kick per shot in base-kick units (x right, y up). Empty = a pattern seeded from this archetype.")]
        public List<Vector2> recoilPattern = new List<Vector2>();
        [Tooltip("Past the end of the pattern, shots loop from this index.")]
        [Min(0)] public int patternLoopFrom = 0;

        [Header("Base Stats (the frame profile)")]
        public float weaponDamage = 0f;
        [Tooltip("Head-hit damage multiplier, applied before the hitbox's own multiplier.")]
        [Min(1f)] public float precisionMultiplier = 1.5f;

        [Tooltip("Fire rate in ROUNDS per minute -- the same meaning for every fire " +
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

        [Tooltip("Shared reserve pool on the player. Empty = reserves live on each weapon.")]
        public AmmoPoolSO ammoPool;

        [Header("Handling")]
        [Tooltip("Base handling for this frame. A per-weapon delta layers on top. " +
         "Reduces equip, stow and ADS time proportionally.")]
        public float handling = 0f;

        [Tooltip("Seconds to bring this weapon up at ZERO handling. Frame identity -- a " +
                 "rocket launcher is slower than a sidearm no matter how it rolls.")]
        public float baseEquipTime = 0.5f;

        [Tooltip("Seconds to put this weapon away at ZERO handling.")]
        public float baseStowTime = 0.4f;


        // Inline rather than a FireModeSO reference -- see FireMode.cs for why.
        [Header("Fire Mode (primary)")]
        public FireMode primaryFire = new FireMode();

        [Header("Presentation")]
        public GameObject crosshairPrefab;
    }
}