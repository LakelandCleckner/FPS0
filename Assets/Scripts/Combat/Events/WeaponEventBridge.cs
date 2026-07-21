using UnityEngine;
using Combat.Sources;
using Combat.Weapons;

namespace Combat.Events
{
    // Forwards WeaponFireController's existing C# events onto the owner bus.
    //
    // WHY A BRIDGE INSTEAD OF PUBLISHING FROM THE CONTROLLER:
    // WeaponFireController already exposes OnFired / OnReloadStarted / OnDryFire, and
    // WeaponAnimator subscribes to them in OnEnable/OnDisable. That path works and
    // is genuinely decoupled ("the fire controller knows nothing about animation").
    // Bridging means neither file is modified, so working fire/reload animation
    // cannot be broken by this change. If the controller published directly we'd be
    // editing a file whose Animator setup has known, documented gotchas.
    //
    // Presentation keeps using the direct C# events — they're local, immediate and
    // allocation-free. The bus exists for the OWNER-SCOPED view that perks need.
    //
    // RELOAD START IS DELIBERATELY NOT BRIDGED. The controller fires OnReloadStarted
    // when it asks for a reload; WeaponAmmo decides whether one actually begins (it
    // refuses on a full mag, on empty reserves, or if one is already running). Doc 07
    // requires a single authoritative origin per event, and for reload that's the
    // state machine, not the requester. Bridging it here would double-fire and would
    // announce reloads that never happened.
    [RequireComponent(typeof(WeaponFireController))]
    public class WeaponEventBridge : MonoBehaviour
    {
        [SerializeField] private WeaponFireController controller;
        [SerializeField] private WeaponDamageSource damageSource;

        private WeaponEventBus bus;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<WeaponFireController>();
            if (damageSource == null && controller != null) damageSource = controller.DamageSource;

            bus = WeaponEventBus.FindFor(this);
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.OnFired += HandleFired;
            controller.OnDryFire += HandleDryFire;
        }

        private void OnDisable()
        {
            if (controller == null) return;
            controller.OnFired -= HandleFired;
            controller.OnDryFire -= HandleDryFire;
        }

        private void HandleFired()
        {
            if (bus == null) return;
            bus.Publish(WeaponEvent.ForWeapon(WeaponEventType.ShotFired, damageSource));
        }

        private void HandleDryFire()
        {
            if (bus == null) return;
            bus.Publish(WeaponEvent.ForWeapon(WeaponEventType.DryFire, damageSource));
        }
    }
}
