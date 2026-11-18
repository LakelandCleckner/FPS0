using UnityEngine;
using TMPro;
using Combat.Events;

namespace Combat.Weapons
{
    // Drives an ammo readout from whichever weapon is currently held.
    //
    // WAS: an Update loop that read three values every frame and compared them
    // against cached copies to decide whether to rebuild the string.
    // THEN: subscribed to the owner bus and rebuilt only when an ammo event said
    // something actually changed.
    // NOW: also RE-TARGETS on equip. Bound to a single serialized WeaponAmmo it kept
    // showing the first weapon's magazine after a swap — the events were routing
    // correctly per weapon, the display was simply listening to the wrong one.
    //
    // Retargeting uses WeaponLoadout's direct C# event rather than the bus. This is
    // presentation: local, immediate, allocation-free. The bus exists for the
    // owner-scoped view perks need.
    public class AmmoDisplay : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Follows whichever weapon this loadout has in hand. Leave empty for a " +
                 "fixed single-weapon readout and assign Ammo instead.")]
        [SerializeField] private WeaponLoadout loadout;

        [Tooltip("Used when there is no loadout, or before its first equip.")]
        [SerializeField] private WeaponAmmo ammo;

        [SerializeField] private TMP_Text label;

        [Header("Element")]
        [Tooltip("Shows the held weapon's damage type icon. Empty = one is created to the left of the label.")]
        [SerializeField] private UnityEngine.UI.Image elementIcon;
        [SerializeField] private bool showElement = true;
        [Tooltip("Icon size as a multiple of the label's font size.")]
        [SerializeField] private float elementIconScale = 1.1f;
        [Tooltip("Gap in pixels between the icon and the label.")]
        [SerializeField] private float elementIconGap = 8f;

        [Header("Format")]
        [Tooltip("Symbol shown for reserves when the weapon has infinite reserves.")]
        [SerializeField] private string infiniteSymbol = "\u221E"; // ∞

        [Tooltip("Separator between magazine and reserves.")]
        [SerializeField] private string separator = " / ";

        private WeaponEventBus bus;
        private WeaponAmmo bound;   // what we're currently subscribed to

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

        private void OnEnable()
        {
            if (loadout != null)
            {
                loadout.OnWeaponEquipped += HandleWeaponEquipped;

                // The loadout may already have equipped before this enabled, in which
                // case the startup event has been and gone — take the active weapon
                // directly. If it hasn't run yet, the event covers us.
                if (loadout.Active != null)
                    Bind(loadout.Active.GetComponent<WeaponAmmo>());
                else
                    Bind(ammo);
            }
            else
            {
                Bind(ammo);
            }
        }

        private void OnDisable()
        {
            if (loadout != null) loadout.OnWeaponEquipped -= HandleWeaponEquipped;
            Bind(null);
        }

        private void HandleWeaponEquipped(WeaponFireController controller)
        {
            Bind(controller != null ? controller.GetComponent<WeaponAmmo>() : null);
        }

        // Moves the subscription from one weapon to another. Idempotent, so the
        // startup double-path above can't produce a duplicate subscription.
        private void Bind(WeaponAmmo next)
        {
            if (next == bound) { Refresh(); return; }

            if (bound != null && bus != null)
            {
                for (int i = 0; i < Watched.Length; i++)
                    bus.Unsubscribe(Watched[i], HandleAmmoEvent, bound.Source);
            }

            bound = next;
            RefreshElement();

            if (bound != null)
            {
                // The bus lives on the combatant root; this label is usually parented
                // to a HUD canvas rather than the player, so search from the weapon.
                if (bus == null) bus = WeaponEventBus.FindFor(bound);

                if (bus != null)
                {
                    for (int i = 0; i < Watched.Length; i++)
                        bus.Subscribe(Watched[i], HandleAmmoEvent, bound.Source);
                }
            }

            // Read current state directly rather than waiting for the next event —
            // the weapon we just picked up has a magazine right now, and nothing is
            // going to announce it until it changes.
            Refresh();
        }

        private void HandleAmmoEvent(WeaponEvent evt) => Refresh();

        // Icon of the held weapon's damage type, tinted to its colour.
        private void RefreshElement()
        {
            var source = bound != null ? bound.Source : null;
            var type = source != null ? source.BaseDamageType : null;
            bool show = showElement && type != null && type.icon != null;
            if (show && elementIcon == null) elementIcon = CreateIcon();
            if (elementIcon == null) return;
            elementIcon.enabled = show;
            if (!show) return;
            elementIcon.sprite = type.icon;
            elementIcon.color = type.color;
        }

        private UnityEngine.UI.Image CreateIcon()
        {
            if (label == null) return null;
            var go = new GameObject("ElementIcon", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(label.rectTransform, false);
            // Just outside the label's left edge, vertically centred
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            float size = label.fontSize * elementIconScale;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-elementIconGap, 0f);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private void Refresh()
        {
            if (label == null) return;

            if (bound == null)
            {
                label.text = string.Empty;
                return;
            }

            string reserveText = bound.InfiniteReserves
                ? infiniteSymbol
                : bound.Reserves.ToString();

            string text = bound.Magazine + separator + reserveText;
            if (bound.IsReloading) text += " …"; // subtle reloading hint

            label.text = text;
        }
    }
}