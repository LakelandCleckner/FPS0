using System;
using System.Collections.Generic;
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
    //
    // Fire rework: owns the shot-id counter, stamps it onto the behaviour's ShotInfo,
    // and picks a delivery per burst index so a burst can vary what it launches.
    //
    // INPUT POLICY lives here, not in WeaponAmmo. The ammo state machine stays
    // ungated so perks can drive it; this component decides what the PLAYER may do
    // with the weapon right now.
    public class WeaponFireController : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private WeaponDamageSource damageSource;
        [SerializeField] private WeaponAmmo ammo;

        [Header("Scene refs injected into delivery")]
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private Transform muzzle;
        // projectilePrefab REMOVED — it lives on ProjectileSO now. A prefab here meant
        // every weapon using this controller fired the same projectile.

        [Header("Input")]
        [SerializeField] private Key reloadKey = Key.R;

        private PlayerAudio playerAudio;

        public event Action OnFired;
        public event Action OnReloadStarted;
        public event Action OnDryFire;

        private IFireBehavior behavior;
        private IDelivery delivery;                       // default for this mode
        private Dictionary<int, IDelivery> burstOverrides; // by burst index, -1 = final
        private bool triggerHeldLast;
        private float reloadBuffer;

        private const float ReloadBufferWindow = 0.2f;

        // PER-WEAPON shot counter. Monotonic for the life of this controller — it
        // deliberately survives BuildFireMode, because resetting it on an equip would
        // let a new shot reuse an id still held by an in-flight projectile or a live
        // DOT entry, which is the collision the id exists to prevent.
        //
        // Ids are unique per weapon, not globally. Perks are routed per weapon by the
        // event bus so that's normally invisible; a perk with a wider scope that
        // compares ids across weapons should key on (DamageSource, ShotId), both of
        // which are on the hit context.
        private int nextShotId = 1;

        // Set by WeaponLoadout. False while stowed or mid-swap. The component keeps
        // running (its weapon can still be reloaded by a perk); it just stops taking
        // player input.
        public bool IsActive { get; set; } = true;

        // Firing specifically. False while sprinting and during the sprint-out
        // recovery, when IsActive is still true — the weapon is in your hands, it
        // just isn't on target yet.
        public bool CanFire { get; set; } = true;

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
            var mode = archetype != null ? archetype.primaryFire : null;

            if (mode == null || mode.behavior == null || mode.delivery == null)
            {
                Debug.LogError("[WeaponFireController] Missing fire mode / behavior / delivery.");
                return;
            }

            behavior = mode.behavior.CreateBehavior();
            behavior.FireRequested = HandleFireRequested;

            var buildCtx = new DeliveryBuildContext(resolver, muzzle);
            delivery = mode.delivery.CreateDelivery(buildCtx);

            // Build any per-burst-index deliveries once, here, rather than per shot.
            burstOverrides = null;
            if (mode.deliveryOverrides != null && mode.deliveryOverrides.Count > 0)
            {
                burstOverrides = new Dictionary<int, IDelivery>(mode.deliveryOverrides.Count);
                foreach (var o in mode.deliveryOverrides)
                {
                    if (o == null || o.delivery == null) continue;
                    var built = o.delivery.CreateDelivery(buildCtx);
                    if (built != null) burstOverrides[o.burstIndex] = built;
                }
            }
        }

        private void Update()
        {
            if (behavior == null) return;

            // Reload intent is buffered BEFORE the gates, and deliberately so.
            // Pressing reload while sprinting cancels the sprint (WeaponLoadout), but
            // PlayerMovement recomputes IsSprinting in its own Update, so IsActive can
            // still be false on the frame the key goes down. wasPressedThisFrame is
            // true for exactly one frame — without a buffer that press is simply lost
            // and the player has to press reload twice to come out of a sprint.
            //
            // Buffering while stowed is harmless: IsActive gates the attempt and the
            // window expires in a fifth of a second.
            if (Keyboard.current != null && Keyboard.current[reloadKey].wasPressedThisFrame)
                reloadBuffer = ReloadBufferWindow;

            if (reloadBuffer > 0f) reloadBuffer -= Time.deltaTime;

            if (!IsActive) return;

            // A burst is committed once started — releasing the trigger doesn't cut it
            // short, so a reload can't interrupt one either. Without this the behaviour
            // freezes mid-burst at the IsReloading gate below and resumes afterwards,
            // firing its remaining shots seconds later. Narrow window, but reachable
            // and it reads as broken.
            bool midBurst = (behavior as BurstFireBehavior)?.Bursting ?? false;

            if (!midBurst && ammo != null && reloadBuffer > 0f)
            {
                if (ammo.BeginReload())
                {
                    reloadBuffer = 0f;
                    PlayReloadAudio();
                    OnReloadStarted?.Invoke();
                }
            }

            if (!CanFire) return;

            // A reload is committed once started, so firing waits rather than
            // cancelling it. The input isn't lost: an auto weapon reads Held live and
            // fires the instant the reload completes. Semi-auto needs a fresh press,
            // because triggerHeldLast stops updating below this line — no banked shot
            // from a trigger you were already holding.
            if (ammo != null && ammo.IsReloading) return;

            bool held = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool pressed = held && !triggerHeldLast;
            bool released = !held && triggerHeldLast;
            triggerHeldLast = held;

            var trigger = new TriggerState(held, pressed, released);
            float rpm = damageSource != null ? damageSource.ResolvedRPM : 0f;
            behavior.Tick(Time.deltaTime, trigger, rpm);
        }

        private void HandleFireRequested(ShotInfo shot)
        {
            if (ammo != null && !ammo.TryConsume())
            {
                var empty = damageSource.EmptyClip;
                if (empty != null && playerAudio != null) playerAudio.Play3D(empty);
                OnDryFire?.Invoke();

                // A burst that runs dry stops rather than dry-firing its remainder:
                // two rounds left on a three-round burst gives two shots and one dry
                // click, not two shots and two clicks.
                (behavior as BurstFireBehavior)?.NotifyShotFailed();
                return;
            }

            // The behaviour doesn't know the id — the controller owns the counter, so
            // the behaviour stays a pure timing machine.
            var stamped = new ShotInfo(nextShotId++, shot.BurstIndex, shot.BurstCount, shot.ChargeLevel);

            var fire = damageSource.FireClip;
            if (fire != null && playerAudio != null) playerAudio.Play3D(fire);

            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            SelectDelivery(in stamped).Fire(ray.origin, ray.direction, damageSource, in stamped);

            OnFired?.Invoke();
        }

        // Exact index first, then -1 meaning "the final shot" — so an override stays
        // correct if the burst count changes.
        private IDelivery SelectDelivery(in ShotInfo shot)
        {
            if (burstOverrides == null) return delivery;

            if (burstOverrides.TryGetValue(shot.BurstIndex, out var exact)) return exact;
            if (shot.IsFinalInBurst && burstOverrides.TryGetValue(-1, out var final)) return final;

            return delivery;
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