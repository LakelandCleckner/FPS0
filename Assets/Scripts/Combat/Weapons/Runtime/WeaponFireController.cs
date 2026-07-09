using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Core;
using Combat.Delivery;
using Combat.Sources;

namespace Combat.Weapons
{
    // The weapon runtime coordinator. Wires the (decoupled) firing behavior and
    // delivery together and drives ammo/reload. Emits events (fired/reload/dry) and
    // knows nothing about animation/presentation.
    //
    // Phase 2f: passes the weapon's RESOLVED RPM (from its StatContainer) to the
    // behavior instead of the retired StatBlock.
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

        public event Action OnFired;
        public event Action OnReloadStarted;
        public event Action OnDryFire;

        private IFireBehavior behavior;
        private IDelivery delivery;
        private bool triggerHeldLast;

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

            if (ammo != null && Keyboard.current != null && Keyboard.current[reloadKey].wasPressedThisFrame)
            {
                if (ammo.BeginReload())
                {
                    PlayReloadAudio();
                    OnReloadStarted?.Invoke();
                }
            }

            bool held = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool pressed = held && !triggerHeldLast;
            bool released = !held && triggerHeldLast;
            triggerHeldLast = held;

            var trigger = new TriggerState(held, pressed, released);
            float rpm = damageSource != null ? damageSource.ResolvedRPM : 0f;
            behavior.Tick(Time.deltaTime, trigger, rpm);
        }

        private void HandleFireRequested()
        {
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