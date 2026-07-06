using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Core;
using Combat.Delivery;
using Combat.Sources;

namespace Combat.Weapons
{
    // The weapon runtime coordinator (replaces HandgunFire). It's the ONLY thing
    // that knows about both firing behavior and delivery — it wires them together
    // and keeps them decoupled from each other:
    //
    //   reads input -> builds a TriggerState -> ticks the fire behavior ->
    //   behavior decides when to fire and invokes FireRequested ->
    //   controller routes that to the delivery.
    //
    // Behavior + delivery are built ONCE from the fire-mode data (cached, reused),
    // with scene refs injected into the delivery. No per-shot allocation.
    public class WeaponFireController : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private WeaponDamageSource damageSource;

        [Header("Scene refs injected into delivery")]
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private Transform muzzle;             // projectile spawn
        [SerializeField] private Projectile projectilePrefab;  // projectile prefab

        [Header("Audio / Animation")]
        [SerializeField] private GameObject weaponModel;       // for fire anim
        [SerializeField] private GameObject crosshair;         // for fire anim
        private PlayerAudio playerAudio;

        // built-once runtime pieces
        private IFireBehavior behavior;
        private IDelivery delivery;

        // trigger edge tracking
        private bool triggerHeldLast;

        private void Awake()
        {
            playerAudio = GetComponentInParent<PlayerAudio>();
        }

        private void Start()
        {
            BuildFireMode();
        }

        // Build behavior + delivery from the archetype's primary fire mode.
        // Called once; call again if the fire mode changes (upgrade later).
        public void BuildFireMode()
        {
            var weapon = damageSource != null ? damageSource.Weapon : null;
            var archetype = weapon != null ? weapon.archetype : null;
            var mode = archetype != null ? archetype.primaryFireMode : null;

            if (mode == null || mode.fireBehavior == null || mode.delivery == null)
            {
                Debug.LogError("[WeaponFireController] Missing fire mode / behavior / delivery.");
                return;
            }

            // behavior (plain class), wired to route its fire signal to delivery
            behavior = mode.fireBehavior.CreateBehavior();
            behavior.FireRequested = HandleFireRequested;

            // delivery (plain class) with scene refs injected
            var buildCtx = new DeliveryBuildContext(resolver, muzzle, projectilePrefab);
            delivery = mode.delivery.CreateDelivery(buildCtx);
        }

        private void Update()
        {
            if (behavior == null) return;

            // read input, build the input-agnostic trigger snapshot
            bool held = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool pressed = held && !triggerHeldLast;
            bool released = !held && triggerHeldLast;
            triggerHeldLast = held;

            var trigger = new TriggerState(held, pressed, released);

            // tick the behavior with resolved stats (RPM etc.)
            var stats = damageSource.GetStats();
            behavior.Tick(Time.deltaTime, trigger, stats);
        }

        // Called by the behavior when a shot should fire. Routes to delivery.
        private void HandleFireRequested()
        {
            // ammo gate (still GlobalAmmo for now — reload system is 1c)
            if (GlobalAmmo.handgunAmmoCount <= 0)
            {
                var empty = damageSource.EmptyClip;
                if (empty != null && playerAudio != null) playerAudio.Play3D(empty);
                return;
            }
            GlobalAmmo.handgunAmmoCount -= 1;

            var fire = damageSource.FireClip;
            if (fire != null && playerAudio != null) playerAudio.Play3D(fire);

            // fire the delivery down the camera ray
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            delivery.Fire(ray.origin, ray.direction, damageSource);

            PlayFireAnims();
        }

        private void PlayFireAnims()
        {
            if (weaponModel != null) weaponModel.GetComponent<Animator>()?.Play("HandgunFire");
            if (crosshair != null) crosshair.GetComponent<Animator>()?.Play("HandgunFireCrosshair");
            Invoke(nameof(ResetAnims), 0.1f);
        }

        private void ResetAnims()
        {
            if (weaponModel != null) weaponModel.GetComponent<Animator>()?.Play("New State");
            if (crosshair != null) crosshair.GetComponent<Animator>()?.Play("New State");
        }

        private void OnDisable()
        {
            behavior?.Reset();
        }
    }
}
