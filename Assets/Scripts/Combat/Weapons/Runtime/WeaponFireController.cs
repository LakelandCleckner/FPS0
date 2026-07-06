using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Core;
using Combat.Delivery;
using Combat.Sources;

namespace Combat.Weapons
{
    // The weapon runtime coordinator. Wires the (decoupled) firing behavior and
    // delivery together and drives ammo/reload. It EMITS EVENTS for what happened
    // (fired, reload started) and knows NOTHING about animation/presentation — a
    // separate WeaponAnimator (or any subscriber) reacts to these. This keeps the
    // controller focused and lets each gun present differently without touching it.
    //
    // (These events are an early, local version of the weapon event surface in the
    // combat design docs; they can migrate onto the event bus later.)
    public class WeaponFireController : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private WeaponDamageSource damageSource;
        [SerializeField] private WeaponAmmo ammo;

        [Header("Scene refs injected into delivery")]
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private Transform muzzle;
        [SerializeField] private Projectile projectilePrefab;

        [Header("Input")]
        [SerializeField] private Key reloadKey = Key.R;

        private PlayerAudio playerAudio;

        // ---- Weapon events (subscribers: animator, future perks/event bus) ----
        public event Action OnFired;          // a shot actually went off
        public event Action OnReloadStarted;  // a reload actually began
        public event Action OnDryFire;        // trigger pulled but no ammo

        private IFireBehavior behavior;
        private IDelivery delivery;
        private bool triggerHeldLast;

        // expose stats for subscribers that need them (e.g. animation RPM scaling)
        public WeaponDamageSource DamageSource => damageSource;

        private void Awake()
        {
            playerAudio = GetComponentInParent<PlayerAudio>();
        }

        private void Start()
        {
            BuildFireMode();
        }

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

            behavior = mode.fireBehavior.CreateBehavior();
            behavior.FireRequested = HandleFireRequested;

            var buildCtx = new DeliveryBuildContext(resolver, muzzle, projectilePrefab);
            delivery = mode.delivery.CreateDelivery(buildCtx);
        }

        private void Update()
        {
            if (behavior == null) return;

            // reload input — only fires the event if a reload actually STARTED
            if (ammo != null && Keyboard.current != null && Keyboard.current[reloadKey].wasPressedThisFrame)
            {
                if (ammo.BeginReload())
                {
                    PlayReloadAudio();
                    OnReloadStarted?.Invoke();
                }
            }

            // trigger snapshot
            bool held = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool pressed = held && !triggerHeldLast;
            bool released = !held && triggerHeldLast;
            triggerHeldLast = held;

            var trigger = new TriggerState(held, pressed, released);
            var stats = damageSource.GetStats();
            behavior.Tick(Time.deltaTime, trigger, stats);
        }

        private void HandleFireRequested()
        {
            // ammo gate — consume a round (also cancels a reload in progress)
            if (ammo != null && !ammo.TryConsume())
            {
                var empty = damageSource.EmptyClip;
                if (empty != null && playerAudio != null) playerAudio.Play3D(empty);
                OnDryFire?.Invoke();
                return;
            }

            var fire = damageSource.FireClip;
            if (fire != null && playerAudio != null) playerAudio.Play3D(fire);

            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            delivery.Fire(ray.origin, ray.direction, damageSource);

            OnFired?.Invoke();
        }

        private void PlayReloadAudio()
        {
            var rc = damageSource.Weapon != null ? damageSource.Weapon.reloadClip : null;
            if (rc != null && playerAudio != null) playerAudio.Play3D(rc);
        }

        private void OnDisable()
        {
            behavior?.Reset();
        }
    }
}