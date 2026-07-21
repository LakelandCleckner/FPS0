using UnityEngine;
using TMPro;
using Combat.Events;

namespace Combat.Weapons
{
    // Drives an ammo readout from a WeaponAmmo. Shows "magazine / reserves", or
    // infinity for infinite reserves.
    //
    // WAS: an Update loop that read three values every frame and compared them
    // against cached copies to decide whether to rebuild the string.
    // NOW: subscribes to the owner bus and rebuilds only when an ammo event says
    // something actually changed. The change-detection fields are gone because the
    // events already carry that information — the poll existed only to synthesise it.
    //
    // This is the first real consumer of the bus, and it's deliberately a boring one:
    // if the readout tracks correctly through firing, reloading, cancelling a reload
    // mid-animation and picking up reserves, dispatch and per-weapon routing are
    // both working before any perk depends on them.
    public class AmmoDisplay : MonoBehaviour
    {
        [SerializeField] private WeaponAmmo ammo;
        [SerializeField] private TMP_Text label;

        [Header("Format")]
        [Tooltip("Symbol shown for reserves when the weapon has infinite reserves.")]
        [SerializeField] private string infiniteSymbol = "\u221E"; // ∞
        [Tooltip("Separator between magazine and reserves.")]
        [SerializeField] private string separator = " / ";

        private WeaponEventBus bus;

        // Every event that can change what this readout shows.
        private static readonly WeaponEventType[] Watched =
        {
            WeaponEventType.AmmoChanged,
            WeaponEventType.ReloadStart,
            WeaponEventType.ReloadComplete,
            WeaponEventType.ReloadCancelled
        };

        private void Reset()
        {
            label = GetComponent<TMP_Text>();
        }

        private void Awake()
        {
            // The bus lives on the combatant root; this label is usually parented to
            // a HUD canvas rather than the player, so search from the weapon instead
            // of from self.
            bus = ammo != null ? WeaponEventBus.FindFor(ammo) : null;
        }

        private void OnEnable()
        {
            if (bus == null || ammo == null) return;

            for (int i = 0; i < Watched.Length; i++)
                bus.Subscribe(Watched[i], HandleAmmoEvent, ammo.Source);

            Refresh();
        }

        private void OnDisable()
        {
            if (bus == null || ammo == null) return;

            for (int i = 0; i < Watched.Length; i++)
                bus.Unsubscribe(Watched[i], HandleAmmoEvent, ammo.Source);
        }

        private void HandleAmmoEvent(WeaponEvent evt) => Refresh();

        private void Refresh()
        {
            if (ammo == null || label == null) return;

            string reserveText = ammo.InfiniteReserves
                ? infiniteSymbol
                : ammo.Reserves.ToString();

            string text = ammo.Magazine + separator + reserveText;
            if (ammo.IsReloading) text += " …"; // subtle reloading hint

            label.text = text;
        }
    }
}