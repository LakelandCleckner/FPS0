using UnityEngine;
using Combat.Sources;

namespace Combat.Weapons
{
    // Presentation component: drives a weapon's Animator in response to
    // WeaponFireController and WeaponLoadout events. All animation concerns live
    // HERE, per-gun — the fire controller and the loadout know nothing about
    // animation. A gun with no animation simply omits this component.
    //
    // Every timed animation scales via a per-state Speed MULTIPLIER param, so a clip
    // authored at one length plays across whatever duration the stats resolve to.
    // Fire fills exactly one fire interval; reload fills reload_time; equip, stow and
    // sprint-exit fill the durations the loadout reports when each begins. Author the
    // clips at whatever length feels right — the code reads their real length at
    // startup and works out the multiplier.
    //
    // ANIMATOR SETUP:
    //   Parameters
    //     Trigger : Fire, Reload, Equip, Stow
    //     Bool    : IsMoving, IsSprinting
    //     Float   : FireSpeed, ReloadSpeed, EquipSpeed, StowSpeed, SprintExitSpeed
    //   States (Speed -> Multiplier -> the matching float param)
    //     Idle (default, loop), Walk (loop), Sprint (loop),
    //     HandgunFire, Reload, Equip, Stow, SprintExit (all one-shot, Loop Time OFF)
    //   Transitions
    //     Any State -> Fire/Reload/Equip/Stow on their triggers, Has Exit Time OFF
    //     one-shots -> Idle, Has Exit Time ON, Fixed Duration OFF
    //     Idle <-> Walk on IsMoving, Idle <-> Sprint on IsSprinting, Exit Time OFF
    //     Sprint -> SprintExit on IsSprinting false, Exit Time OFF
    //     SprintExit -> Idle, Has Exit Time ON
    //
    //   Fixed Duration must be OFF on every return transition, or the blend won't
    //   scale with playback speed and a stat-driven duration will desync.
    public class WeaponAnimator : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private WeaponFireController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private WeaponDamageSource damageSource;

        [Tooltip("Optional. Found in parents if empty. Drives equip/stow/sprint-exit.")]
        [SerializeField] private WeaponLoadout loadout;

        [Tooltip("Optional. Found in parents if empty. Drives the walk/sprint bools.")]
        [SerializeField] private PlayerMovement playerMovement;

        [Header("Clip names (for length reading)")]
        [SerializeField] private string fireClipName = "HandgunFire";
        [SerializeField] private string reloadClipName = "Reload";
        [SerializeField] private string equipClipName = "Equip";
        [SerializeField] private string stowClipName = "Stow";
        [SerializeField] private string sprintExitClipName = "SprintExit";

        [Header("Scaling")]
        [Tooltip("Scale the fire animation so one recoil fills one shot interval.")]
        [SerializeField] private bool scaleFireToRPM = true;
        [Tooltip("Scale the reload animation to match reloadTime.")]
        [SerializeField] private bool scaleReloadToTime = true;
        [Tooltip("Scale equip/stow/sprint-exit to the durations the loadout reports.")]
        [SerializeField] private bool scaleTransitions = true;

        private static readonly int FireTrigger = Animator.StringToHash("Fire");
        private static readonly int ReloadTrigger = Animator.StringToHash("Reload");
        private static readonly int EquipTrigger = Animator.StringToHash("Equip");
        private static readonly int StowTrigger = Animator.StringToHash("Stow");

        private static readonly int FireSpeedParam = Animator.StringToHash("FireSpeed");
        private static readonly int ReloadSpeedParam = Animator.StringToHash("ReloadSpeed");
        private static readonly int EquipSpeedParam = Animator.StringToHash("EquipSpeed");
        private static readonly int StowSpeedParam = Animator.StringToHash("StowSpeed");
        private static readonly int SprintExitSpeedParam = Animator.StringToHash("SprintExitSpeed");

        private static readonly int IsMovingParam = Animator.StringToHash("IsMoving");
        private static readonly int IsSprintingParam = Animator.StringToHash("IsSprinting");

        private float fireClipLength;
        private float reloadClipLength;
        private float equipClipLength;
        private float stowClipLength;
        private float sprintExitClipLength;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (controller == null) controller = GetComponentInParent<WeaponFireController>();
            if (damageSource == null && controller != null) damageSource = controller.DamageSource;
            if (loadout == null) loadout = GetComponentInParent<WeaponLoadout>();
            if (playerMovement == null) playerMovement = GetComponentInParent<PlayerMovement>();

            fireClipLength = ReadClipLength(fireClipName);
            reloadClipLength = ReadClipLength(reloadClipName);
            equipClipLength = ReadClipLength(equipClipName);
            stowClipLength = ReadClipLength(stowClipName);
            sprintExitClipLength = ReadClipLength(sprintExitClipName);
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.OnFired += HandleFired;
                controller.OnReloadStarted += HandleReloadStarted;
            }

            if (loadout != null)
            {
                loadout.OnEquipStarted += HandleEquipStarted;
                loadout.OnStowStarted += HandleStowStarted;
                loadout.OnSprintExitStarted += HandleSprintExitStarted;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnFired -= HandleFired;
                controller.OnReloadStarted -= HandleReloadStarted;
            }

            if (loadout != null)
            {
                loadout.OnEquipStarted -= HandleEquipStarted;
                loadout.OnStowStarted -= HandleStowStarted;
                loadout.OnSprintExitStarted -= HandleSprintExitStarted;
            }
        }

        // Locomotion is a continuous condition, so it's a bool read each frame rather
        // than an event. Cheap: SetBool no-ops when the value is unchanged.
        private void Update()
        {
            if (animator == null || playerMovement == null) return;

            animator.SetBool(IsSprintingParam, playerMovement.IsSprinting);
            animator.SetBool(IsMovingParam, playerMovement.IsMoving);
        }

        private void HandleFired()
        {
            if (animator == null) return;

            if (scaleFireToRPM && fireClipLength > 0f && damageSource != null)
            {
                float rpm = damageSource.ResolvedRPM;
                if (rpm > 0f)
                {
                    float interval = 60f / rpm;
                    animator.SetFloat(FireSpeedParam, fireClipLength / interval);
                }
            }

            animator.SetTrigger(FireTrigger);
        }

        private void HandleReloadStarted()
        {
            if (animator == null) return;

            if (scaleReloadToTime && reloadClipLength > 0f && damageSource != null)
            {
                float reloadTime = damageSource.ResolvedReloadTime;
                if (reloadTime > 0f)
                    animator.SetFloat(ReloadSpeedParam, reloadClipLength / reloadTime);
            }

            animator.SetTrigger(ReloadTrigger);
        }

        // The loadout raises these for whichever weapon is transitioning, so each
        // animator ignores the ones that aren't about its own gun.
        private void HandleEquipStarted(WeaponFireController c, float duration)
        {
            if (c != controller || animator == null) return;

            SetTransitionSpeed(EquipSpeedParam, equipClipLength, duration);

            // Rebind happens in WeaponLoadout the moment the weapon becomes visible,
            // and it clears queued triggers — so this must be set AFTER, which it is:
            // the event fires once the incoming weapon is already shown.
            animator.SetTrigger(EquipTrigger);
        }

        private void HandleStowStarted(WeaponFireController c, float duration)
        {
            if (c != controller || animator == null) return;

            SetTransitionSpeed(StowSpeedParam, stowClipLength, duration);
            animator.SetTrigger(StowTrigger);
        }

        // No trigger — the Sprint -> SprintExit transition fires off the IsSprinting
        // bool that Update is already writing. This only sets the playback speed so
        // the clip finishes exactly when the weapon becomes firable again.
        private void HandleSprintExitStarted(WeaponFireController c, float duration)
        {
            if (c != controller || animator == null) return;
            SetTransitionSpeed(SprintExitSpeedParam, sprintExitClipLength, duration);
        }

        private void SetTransitionSpeed(int param, float clipLength, float duration)
        {
            if (!scaleTransitions || clipLength <= 0f || duration <= 0f) return;
            animator.SetFloat(param, clipLength / duration);
        }

        private float ReadClipLength(string clipName)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(clipName))
                return 0f;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip != null && clip.name == clipName)
                    return clip.length;
            return 0f;
        }
    }
}