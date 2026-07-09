using UnityEngine;
using Combat.Sources;

namespace Combat.Weapons
{
    // Presentation component: drives a weapon's Animator in response to
    // WeaponFireController events. All animation concerns live HERE, per-gun — the
    // fire controller knows nothing about animation. A gun with no animation simply
    // omits this component.
    //
    // Fire/reload animations scale via per-state Speed MULTIPLIER params so scaling
    // only affects those states (not idle). The fire animation is scaled so one
    // recoil fills exactly one fire interval — one clean recoil per shot at any RPM.
    //
    // ANIMATOR SETUP:
    //  - float param "FireSpeed"; HandgunFire state: Speed Multiplier -> FireSpeed
    //  - float param "ReloadSpeed"; Reload state: Speed Multiplier -> ReloadSpeed
    //  - Idle -> HandgunFire transition on "Fire" trigger, Has Exit Time OFF
    //  - HandgunFire -> Idle transition, Has Exit Time ON, Fixed Duration OFF
    //    (Fixed Duration must be OFF or the return timing won't scale with speed.)
    public class WeaponAnimator : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private WeaponFireController controller;
        [SerializeField] private Animator animator;
        [SerializeField] private WeaponDamageSource damageSource;

        [Header("Clip names (for length reading)")]
        [SerializeField] private string fireClipName = "HandgunFire";
        [SerializeField] private string reloadClipName = "Reload";

        [Header("Scaling")]
        [Tooltip("Scale the fire animation so one recoil fills one shot interval.")]
        [SerializeField] private bool scaleFireToRPM = true;
        [Tooltip("Scale the reload animation to match reloadTime.")]
        [SerializeField] private bool scaleReloadToTime = true;

        private static readonly int FireTrigger = Animator.StringToHash("Fire");
        private static readonly int ReloadTrigger = Animator.StringToHash("Reload");
        private static readonly int FireSpeedParam = Animator.StringToHash("FireSpeed");
        private static readonly int ReloadSpeedParam = Animator.StringToHash("ReloadSpeed");

        private float fireClipLength;
        private float reloadClipLength;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (controller == null) controller = GetComponentInParent<WeaponFireController>();
            if (damageSource == null && controller != null) damageSource = controller.DamageSource;

            fireClipLength = ReadClipLength(fireClipName);
            reloadClipLength = ReadClipLength(reloadClipName);
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.OnFired += HandleFired;
                controller.OnReloadStarted += HandleReloadStarted;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnFired -= HandleFired;
                controller.OnReloadStarted -= HandleReloadStarted;
            }
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