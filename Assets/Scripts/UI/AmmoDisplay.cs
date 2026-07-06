using UnityEngine;
using TMPro;

namespace Combat.Weapons
{
    // Drives an ammo readout from a WeaponAmmo (replaces the old GlobalAmmo -> TMP
    // text hookup). Shows "magazine / reserves", or infinity for infinite reserves.
    // Updates only when the values change (cheap; no per-frame string alloc unless
    // something changed).
    public class AmmoDisplay : MonoBehaviour
    {
        [SerializeField] private WeaponAmmo ammo;
        [SerializeField] private TMP_Text label;

        [Header("Format")]
        [Tooltip("Symbol shown for reserves when the weapon has infinite reserves.")]
        [SerializeField] private string infiniteSymbol = "\u221E"; // ∞
        [Tooltip("Separator between magazine and reserves.")]
        [SerializeField] private string separator = " / ";

        private int lastMag = int.MinValue;
        private int lastReserves = int.MinValue;
        private bool lastReloading;

        private void Reset()
        {
            label = GetComponent<TMP_Text>();
        }

        private void Update()
        {
            if (ammo == null || label == null) return;

            int mag = ammo.Magazine;
            int res = ammo.Reserves;
            bool reloading = ammo.IsReloading;

            // only rebuild the string when something changed
            if (mag == lastMag && res == lastReserves && reloading == lastReloading)
                return;

            lastMag = mag;
            lastReserves = res;
            lastReloading = reloading;

                string reserveText = ammo.InfiniteReserves ? infiniteSymbol : res.ToString();
            string text = mag + separator + reserveText;
            if (reloading) text += " …"; // subtle reloading hint

            label.text = text;
        }
    }
}
