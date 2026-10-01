using System;
using UnityEngine;
using Combat.Stats;
using Combat.Weapons;

// Aim down sights. One progress value (0 hip .. 1 aimed) drives world FOV, the
// arms' Aiming state and sensitivity scaling, so all three stay in lockstep.
//
// Zoom is MAGNIFICATION (ads_zoom stat), applied to the player's own hip FOV, so
// ADS respects their FOV setting. FOV interpolates in tan-space, which reads as a
// linear zoom rather than one that rushes at the end.
//
// Aim is suppressed while swapping, sprinting or reloading. Pressing aim cancels a
// sprint; a held aim button resumes aiming when the suppression ends.
public class PlayerAim : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GameSettingsSO settings;
    [SerializeField] private WeaponLoadout loadout;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private MouseLook mouseLook;
    [Tooltip("The main (world) camera. Its FOV zooms.")]
    [SerializeField] private Camera worldCamera;
    [Tooltip("Overlay camera that renders only the Viewmodel layer. Fixed FOV.")]
    [SerializeField] private Camera viewmodelCamera;

    [Header("Stats")]
    [SerializeField] private CombatantStats combatantStats;
    [SerializeField] private MovementStatKeys movementStatKeys;

    [Header("Fallbacks (weapon without ADS data)")]
    [SerializeField] private float fallbackAdsTime = 0.25f;
    [SerializeField] private float fallbackZoom = 1.2f;

    // aiming, duration to reach the new target from the current progress
    public event Action<bool, float> AimChanged;

    public bool IsAiming { get; private set; }
    public float Progress { get; private set; }     // linear 0..1 over ads_time

    // Progress shaped by the archetype's ADS curve. The ONE value both the world FOV
    // and the arms pose (AimProgress) read, so they can never drift apart.
    public float Blend { get; private set; }

    private PlayerInputs input;
    private readonly InputLatch latch = new InputLatch();
    private bool wasSuppressed;
    private ModifierHandle moveHandle = ModifierHandle.None;

    private static readonly AnimationCurve LinearCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private void Awake()
    {
        input = new PlayerInputs();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (loadout == null) loadout = GetComponent<WeaponLoadout>();
        if (combatantStats == null) combatantStats = GetComponent<CombatantStats>();
    }

    private void OnEnable() => input.Enable();
    private void OnDisable()
    {
        input.Disable();
        SetMovePenalty(false, 1f);
    }

    private void Update()
    {
        if (settings == null) return;

        latch.Update(input.Player.Aim.IsPressed(), settings.aimInputMode, settings.hybridHoldThreshold);

        // Pressing aim during a sprint drops the sprint.
        if (latch.PressedThisFrame && movement != null && movement.IsSprinting)
            movement.CancelSprint();

        var weapon = loadout != null ? loadout.Active : null;
        var ammo = weapon != null ? weapon.GetComponent<WeaponAmmo>() : null;
        var source = weapon != null ? weapon.DamageSource : null;
        var archetype = source != null && source.Weapon != null ? source.Weapon.archetype : null;

        bool suppressed = weapon == null
                          || !weapon.IsActive                         // swapping or sprinting
                          || (ammo != null && ammo.IsReloading)
                          || (movement != null && movement.IsSprinting);

        if (suppressed && !wasSuppressed) latch.Clear();
        wasSuppressed = suppressed;

        float inTime = Mathf.Max(0.01f, source != null ? source.ResolvedAdsTime : fallbackAdsTime);
        float outTime = Mathf.Max(0.01f, inTime * (archetype != null ? archetype.adsExitFraction : 1f));

        bool want = latch.Active && !suppressed;
        if (want != IsAiming)
        {
            IsAiming = want;
            float duration = want ? (1f - Progress) * inTime : Progress * outTime;
            AimChanged?.Invoke(want, duration);
            SetMovePenalty(want, archetype != null ? archetype.adsMoveSpeedMultiplier : 1f);
        }

        float rate = 1f / (IsAiming ? inTime : outTime);
        Progress = Mathf.MoveTowards(Progress, IsAiming ? 1f : 0f, rate * Time.deltaTime);

        var curve = archetype != null && archetype.adsCurve != null && archetype.adsCurve.length > 0
            ? archetype.adsCurve : LinearCurve;
        float zoom = Mathf.Max(1f, source != null ? source.ResolvedAdsZoom : fallbackZoom);

        Blend = curve.Evaluate(Progress);
        ApplyView(Blend, zoom);
    }

    private void ApplyView(float blend, float zoom)
    {
        if (worldCamera == null) return;

        float hipV = settings.WorldVerticalFov(worldCamera.aspect) * Mathf.Deg2Rad;
        float tanHip = Mathf.Tan(hipV * 0.5f);
        float tanCur = Mathf.Lerp(tanHip, tanHip / zoom, blend);

        worldCamera.fieldOfView = 2f * Mathf.Atan(tanCur) * Mathf.Rad2Deg;
        if (viewmodelCamera != null) viewmodelCamera.fieldOfView = settings.viewmodelFov;

        if (mouseLook != null)
            mouseLook.SensitivityScale = SensitivityScale(tanHip, tanCur, blend);
    }

    private float SensitivityScale(float tanHip, float tanCur, float blend)
    {
        float scale;
        if (settings.adsSensitivityMode == GameSettingsSO.AdsSensitivityMode.MatchFov)
        {
            float md = settings.monitorDistance;
            scale = md <= 0f
                ? tanCur / tanHip
                : Mathf.Atan(md * tanCur) / Mathf.Atan(md * tanHip);
        }
        else
        {
            scale = Mathf.Lerp(1f, settings.adsFlatMultiplier, blend);
        }

        return scale * Mathf.Lerp(1f, settings.adsSensitivityMultiplier, blend);
    }

    private void SetMovePenalty(bool on, float multiplier)
    {
        var c = combatantStats != null ? combatantStats.Container : null;
        if (c == null || movementStatKeys == null || movementStatKeys.moveSpeed == null) return;

        if (moveHandle.IsValid)
        {
            c.RemoveModifier(moveHandle);
            moveHandle = ModifierHandle.None;
        }

        if (on && !Mathf.Approximately(multiplier, 1f))
            moveHandle = c.AddModifier(
                new StatModifier(movementStatKeys.moveSpeed, StatResolver.MULTIPLICATIVE, multiplier - 1f), this);
    }
}