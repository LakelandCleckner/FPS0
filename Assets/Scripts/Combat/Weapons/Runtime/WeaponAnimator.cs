using System.Collections.Generic;
using UnityEngine;
using Combat.Sources;

namespace Combat.Weapons
{
    // Presentation: drives the SHARED arms Animator (ViewmodelRig) and this weapon's
    // own gun Animator from WeaponFireController / WeaponAmmo / WeaponLoadout events.
    // The fire controller, ammo and loadout know nothing about animation.
    //
    // Every timed clip scales through a Speed-multiplier param so it fills the
    // stat-driven duration. Arms and gun clips differ in length, so each Animator
    // gets its own multiplier from its own clip length. Lengths come from the
    // override controllers via ViewmodelRig's base clips — no clip-name strings.
    //
    // ARMS OWNERSHIP: only the weapon the rig is currently running (rig.Current)
    // touches the arms. A stowed weapon reloaded by a holster perk must not play a
    // reload on arms holding a different gun.
    //
    // PARAMETERS (arms; the gun uses the subset it needs — missing params are skipped)
    //   Trigger : Fire, Reload, ReloadCancel, Equip, Stow
    //   Float   : Speed (0 idle, 1 walk, 2 sprint), FireSpeed, ReloadSpeed,
    //             ReloadStartSpeed, ReloadStepSpeed, ReloadEndSpeed, EquipSpeed, StowSpeed
    //             AimProgress (0 hip .. 1 aimed — drives AimRaise's Motion Time)
    //   Int     : ReloadType (0 magazine, 1 per-shell)
    //   Bool    : Reloading (another shell follows), Empty (gun: mag is empty),
    //             Aiming, Crouching
    //
    // Fixed Duration must be OFF on every exit-time return transition, or blends
    // won't scale with playback speed and stat-driven durations desync.
    public class WeaponAnimator : MonoBehaviour
    {
        [Header("Refs (found automatically if empty)")]
        [SerializeField] private WeaponFireController controller;
        [SerializeField] private WeaponAmmo ammo;
        [SerializeField] private WeaponLoadout loadout;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerAim playerAim;
        [SerializeField] private PlayerCrouch playerCrouch;

        [Header("Scaling")]
        [Tooltip("Scale the fire animation so one recoil fills one shot interval.")]
        [SerializeField] private bool scaleFireToRPM = true;
        [Tooltip("Scale reload animations to the resolved reload durations.")]
        [SerializeField] private bool scaleReloadToTime = true;
        [Tooltip("Scale equip/stow to the durations the loadout reports.")]
        [SerializeField] private bool scaleTransitions = true;

        private static readonly int FireTrigger = Animator.StringToHash("Fire");
        private static readonly int ReloadTrigger = Animator.StringToHash("Reload");
        private static readonly int ReloadCancelTrigger = Animator.StringToHash("ReloadCancel");
        private static readonly int EquipTrigger = Animator.StringToHash("Equip");
        private static readonly int StowTrigger = Animator.StringToHash("Stow");

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int FireSpeedParam = Animator.StringToHash("FireSpeed");
        private static readonly int ReloadSpeedParam = Animator.StringToHash("ReloadSpeed");
        private static readonly int ReloadStartSpeedParam = Animator.StringToHash("ReloadStartSpeed");
        private static readonly int ReloadStepSpeedParam = Animator.StringToHash("ReloadStepSpeed");
        private static readonly int ReloadEndSpeedParam = Animator.StringToHash("ReloadEndSpeed");
        private static readonly int EquipSpeedParam = Animator.StringToHash("EquipSpeed");
        private static readonly int StowSpeedParam = Animator.StringToHash("StowSpeed");

        private static readonly int ReloadTypeParam = Animator.StringToHash("ReloadType");
        private static readonly int ReloadingParam = Animator.StringToHash("Reloading");
        private static readonly int EmptyParam = Animator.StringToHash("Empty");
        private static readonly int AimingParam = Animator.StringToHash("Aiming");
        private static readonly int CrouchingParam = Animator.StringToHash("Crouching");
        private static readonly int AimProgressParam = Animator.StringToHash("AimProgress");

        // One Animator plus the clip lengths THIS weapon plays on it.
        private sealed class Target
        {
            public Animator Anim;
            public float Fire, Reload, ReloadStart, ReloadStep, ReloadEnd, Equip, Stow;
            private HashSet<int> parameters;

            public bool Live => Anim != null && Anim.isActiveAndEnabled
                                && Anim.runtimeAnimatorController != null;

            public void Read(RuntimeAnimatorController rc, ViewmodelRig.ClipRoles roles)
            {
                if (roles == null) return;
                Fire = ViewmodelRig.LengthOf(rc, roles.fire);
                Reload = ViewmodelRig.LengthOf(rc, roles.reload);
                ReloadStart = ViewmodelRig.LengthOf(rc, roles.reloadStart);
                ReloadStep = ViewmodelRig.LengthOf(rc, roles.reloadStep);
                ReloadEnd = ViewmodelRig.LengthOf(rc, roles.reloadEnd);
                Equip = ViewmodelRig.LengthOf(rc, roles.equip);
                Stow = ViewmodelRig.LengthOf(rc, roles.stow);
            }

            // Built lazily: Animator.parameters is only valid on an enabled Animator.
            // Every override of a base controller shares its parameter set, so one
            // cache survives controller swaps on the shared arms.
            private bool Has(int hash)
            {
                if (!Live) return false;
                if (parameters == null)
                {
                    parameters = new HashSet<int>();
                    foreach (var p in Anim.parameters) parameters.Add(p.nameHash);
                }
                return parameters.Contains(hash);
            }

            public void Trigger(int h) { if (Has(h)) Anim.SetTrigger(h); }
            public void Float(int h, float v) { if (Has(h)) Anim.SetFloat(h, v); }
            public void Int(int h, int v) { if (Has(h)) Anim.SetInteger(h, v); }
            public void Bool(int h, bool v) { if (Has(h)) Anim.SetBool(h, v); }
        }

        private readonly Target gun = new Target();
        private readonly Target arms = new Target();
        private ViewmodelRig rig;
        private WeaponDamageSource damageSource;

        // Sprint-exit ramp for Speed (Run -> Hip over the exact sprint-out time).
        private float exitDuration;
        private float exitTimer;
        private float exitFrom;
        private float lastSpeed;

        private bool DrivesArms => rig != null && rig.Current == controller && arms.Live;

        private void Awake()
        {
            if (controller == null) controller = GetComponentInParent<WeaponFireController>();
            if (ammo == null && controller != null) ammo = controller.GetComponent<WeaponAmmo>();
            if (loadout == null) loadout = GetComponentInParent<WeaponLoadout>();
            if (playerMovement == null) playerMovement = GetComponentInParent<PlayerMovement>();
            if (playerAim == null) playerAim = GetComponentInParent<PlayerAim>();
            if (playerCrouch == null) playerCrouch = GetComponentInParent<PlayerCrouch>();
            damageSource = controller != null ? controller.DamageSource : null;
        }

        // Start, not Awake: the loadout spawns gun models in its Awake.
        private void Start()
        {
            rig = loadout != null ? loadout.Rig : null;
            var vm = damageSource != null && damageSource.Weapon != null
                ? damageSource.Weapon.viewmodel : null;

            if (vm == null || rig == null)
            {
                Debug.LogError($"[WeaponAnimator] '{name}' needs a WeaponViewmodelSO on its " +
                               "WeaponSO and a ViewmodelRig on the loadout.");
                enabled = false;
                return;
            }

            arms.Anim = rig.Arms;
            arms.Read(vm.armsController, rig.armsBaseClips);

            gun.Anim = loadout.GunAnimatorOf(controller);
            if (gun.Anim != null)
                gun.Read(gun.Anim.runtimeAnimatorController, rig.gunBaseClips);
        }

        private void OnEnable()
        {
            if (controller != null) controller.OnFired += HandleFired;

            if (ammo != null)
            {
                ammo.ReloadStarted += HandleReloadStarted;
                ammo.ReloadCancelled += HandleReloadCancelled;
                ammo.ReloadInterrupted += HandleReloadInterrupted;
            }

            if (loadout != null)
            {
                loadout.OnEquipStarted += HandleEquipStarted;
                loadout.OnStowStarted += HandleStowStarted;
                loadout.OnSprintExitStarted += HandleSprintExitStarted;
            }

            if (playerAim != null) playerAim.AimChanged += HandleAimChanged;
        }

        private void OnDisable()
        {
            if (controller != null) controller.OnFired -= HandleFired;

            if (ammo != null)
            {
                ammo.ReloadStarted -= HandleReloadStarted;
                ammo.ReloadCancelled -= HandleReloadCancelled;
                ammo.ReloadInterrupted -= HandleReloadInterrupted;
            }

            if (loadout != null)
            {
                loadout.OnEquipStarted -= HandleEquipStarted;
                loadout.OnStowStarted -= HandleStowStarted;
                loadout.OnSprintExitStarted -= HandleSprintExitStarted;
            }

            if (playerAim != null) playerAim.AimChanged -= HandleAimChanged;
        }

        // Continuous conditions. Set* no-ops when the value is unchanged.
        private void Update()
        {
            if (ammo != null)
            {
                gun.Bool(EmptyParam, ammo.Magazine <= 0);

                if (ammo.IsShellReload)
                {
                    bool more = ammo.ShellWillContinue;
                    gun.Bool(ReloadingParam, more);
                    if (DrivesArms) arms.Bool(ReloadingParam, more);
                }
            }

            if (DrivesArms)
            {
                if (playerMovement != null) arms.Float(SpeedParam, LocomotionSpeed());

                // Re-asserted every frame: a rebind on equip resets parameters.
                // AimProgress IS the arms' aim pose (AimRaise Motion Time), so a
                // release at any point reverses from exactly that point.
                if (playerAim != null)
                {
                    arms.Bool(AimingParam, playerAim.IsAiming);
                    arms.Float(AimProgressParam, playerAim.Blend);
                }
                if (playerCrouch != null) arms.Bool(CrouchingParam, playerCrouch.IsCrouched);
            }
        }

        // Set on the edge too, not just in Update, so the state machine sees the
        // flip on the same frame PlayerAim decides it.
        private void HandleAimChanged(bool aiming, float duration)
        {
            if (!DrivesArms) return;
            arms.Bool(AimingParam, aiming);
            arms.Float(AimProgressParam, playerAim.Blend);
        }

        // 0..1 = idle..walk, 1..2 = walk..sprint, from ACTUAL speed so it follows the
        // movement's own acceleration. During a sprint-out, ramps linearly from where
        // it was down to the live value over the loadout's sprint-exit duration.
        private float LocomotionSpeed()
        {
            float walk = Mathf.Max(0.01f, playerMovement.WalkSpeed);
            float sprintRange = Mathf.Max(0.01f, walk * (playerMovement.SprintMultiplier - 1f));
            float s = playerMovement.CurrentSpeed;

            float value = s <= walk ? s / walk : 1f + (s - walk) / sprintRange;
            value = Mathf.Clamp(value, 0f, 2f);

            if (exitTimer > 0f)
            {
                exitTimer -= Time.deltaTime;
                float t = 1f - Mathf.Clamp01(exitTimer / exitDuration);
                value = Mathf.Lerp(exitFrom, Mathf.Min(value, 1f), t);
            }

            lastSpeed = value;
            return value;
        }

        private void HandleFired()
        {
            if (scaleFireToRPM && damageSource != null)
            {
                float rpm = damageSource.ResolvedRPM;
                if (rpm > 0f)
                {
                    float interval = 60f / rpm;
                    gun.Float(FireSpeedParam, SpeedFor(gun.Fire, interval));
                    if (DrivesArms) arms.Float(FireSpeedParam, SpeedFor(arms.Fire, interval));
                }
            }

            gun.Trigger(FireTrigger);
            if (DrivesArms) arms.Trigger(FireTrigger);
        }

        private void HandleReloadStarted()
        {
            ApplyReload(gun);
            if (DrivesArms) ApplyReload(arms);
        }

        private void ApplyReload(Target t)
        {
            bool shell = ammo.IsShellReload;
            t.Int(ReloadTypeParam, shell ? 1 : 0);

            if (scaleReloadToTime)
            {
                if (shell)
                {
                    t.Float(ReloadStartSpeedParam, SpeedFor(t.ReloadStart, ammo.ShellStartDuration));
                    t.Float(ReloadStepSpeedParam, SpeedFor(t.ReloadStep, ammo.ShellDuration));
                    t.Float(ReloadEndSpeedParam, SpeedFor(t.ReloadEnd, ammo.ShellEndDuration));
                }
                else
                {
                    t.Float(ReloadSpeedParam, SpeedFor(t.Reload, ammo.ReloadDuration));
                }
            }

            if (shell) t.Bool(ReloadingParam, ammo.ShellWillContinue);
            t.Trigger(ReloadTrigger);
        }

        private void HandleReloadCancelled()
        {
            gun.Trigger(ReloadCancelTrigger);
            if (DrivesArms) arms.Trigger(ReloadCancelTrigger);
        }

        // Shell reload interrupted by fire: skip End, blend straight out over the
        // handling-driven ready time. Cross-fade from code because the duration is
        // stat-driven, which a fixed transition can't express.
        private void HandleReloadInterrupted(float readyTime)
        {
            if (gun.Live)
                gun.Anim.CrossFadeInFixedTime(rig.GunRestStateHash, readyTime, 0);

            if (DrivesArms && rig.ArmsActionsLayer >= 0)
                arms.Anim.CrossFadeInFixedTime(rig.ArmsEmptyStateHash, readyTime, rig.ArmsActionsLayer);
        }

        // The loadout raises these for whichever weapon is transitioning.
        private void HandleEquipStarted(WeaponFireController c, float duration)
        {
            if (c != controller || !DrivesArms) return;
            if (scaleTransitions) arms.Float(EquipSpeedParam, SpeedFor(arms.Equip, duration));
            arms.Trigger(EquipTrigger);
        }

        private void HandleStowStarted(WeaponFireController c, float duration)
        {
            if (c != controller || !DrivesArms) return;
            if (scaleTransitions) arms.Float(StowSpeedParam, SpeedFor(arms.Stow, duration));
            arms.Trigger(StowTrigger);
        }

        private void HandleSprintExitStarted(WeaponFireController c, float duration)
        {
            if (c != controller || duration <= 0f) return;
            exitDuration = duration;
            exitTimer = duration;
            exitFrom = lastSpeed;
        }

        // Playback multiplier that makes a clip of `length` fill `duration`.
        private static float SpeedFor(float length, float duration)
        {
            if (length <= 0f || duration <= 0f) return 1f;
            return Mathf.Min(length / duration, 100f);
        }
    }
}