using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Combat.Loadout;
using Combat.Loot;
using Combat.Perks;

namespace Combat.UI
{
    // Stacking HUD rows for picked-up items: rarity bar, name in its rarity colour, kind line, and a Vault tag on overflow.
    [RequireComponent(typeof(RectTransform))]
    public class PickupToast : MonoBehaviour
    {
        [SerializeField] private UIStyleSO style;
        [Tooltip("Only this inventory's pickups show. Found on the local player if empty.")]
        [SerializeField] private PlayerInventory inventory;

        [Header("Timing")]
        [Min(0.1f)] [SerializeField] private float lifetime = 3f;
        [Min(0f)] [SerializeField] private float fadeOut = 0.4f;
        [Min(0f)] [SerializeField] private float slideIn = 0.15f;

        [Header("Layout")]
        [Min(1)] [SerializeField] private int maxRows = 5;
        [SerializeField] private float rowHeight = 54f;
        [SerializeField] private float spacing = 6f;
        [Tooltip("How far rows slide in from the right.")]
        [SerializeField] private float slideDistance = 40f;

        private sealed class Row
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public float Age;
            public float Y;
        }

        private readonly List<Row> rows = new List<Row>();   // newest first

        private void OnEnable() => ItemDrop.Collected += HandleCollected;
        private void OnDisable() => ItemDrop.Collected -= HandleCollected;

        private void HandleCollected(PlayerInventory from, object item, bool toVault)
        {
            if (inventory == null) inventory = LocalInventory();
            if (inventory != null && from != inventory) return;
            Show(item, toVault);
        }

        private static PlayerInventory LocalInventory()
        {
            var router = PlayerInputRouter.Local;
            if (router != null)
            {
                var inv = router.GetComponentInParent<PlayerInventory>();
                if (inv == null) inv = router.GetComponentInChildren<PlayerInventory>();
                if (inv != null) return inv;
            }
            return FindFirstObjectByType<PlayerInventory>();
        }

        public void Show(object item, bool toVault)
        {
            if (style == null || item == null) return;

            while (rows.Count >= maxRows) Remove(rows.Count - 1);
            var row = Build(item, toVault);
            rows.Insert(0, row);
            Place(row, 0f, true);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                var r = rows[i];
                r.Age += dt;
                if (r.Age >= lifetime) { Remove(i); continue; }

                float fadeIn = slideIn > 0f ? Mathf.Clamp01(r.Age / slideIn) : 1f;
                float fade = fadeOut > 0f ? Mathf.Clamp01((lifetime - r.Age) / fadeOut) : 1f;
                r.Group.alpha = Mathf.Min(fadeIn, fade);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                float target = i * (rowHeight + spacing);
                r.Y = Mathf.Lerp(r.Y, target, 1f - Mathf.Exp(-18f * dt));
                Place(r, slideIn > 0f ? 1f - Mathf.Clamp01(r.Age / slideIn) : 0f, false);
            }
        }

        // Bottom-right anchored, stacking upward; slide is 1 at spawn and 0 once in place.
        private void Place(Row r, float slide, bool snap)
        {
            if (snap) r.Y = 0f;
            float ease = slide * slide;
            r.Rect.anchoredPosition = new Vector2(ease * slideDistance, r.Y);
        }

        private void Remove(int index)
        {
            var r = rows[index];
            rows.RemoveAt(index);
            if (r.Rect != null) Destroy(r.Rect.gameObject);
        }

        private Row Build(object item, bool toVault)
        {
            var s = style;
            var rarity = ItemText.RarityOf(item);
            Color rc = rarity.HasValue ? s.RarityColor(rarity.Value) : s.text;

            var rt = UIKit.Node(transform, "Pickup");
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(0f, rowHeight);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var bg = UIKit.Box(rt, "Back", s, s.panel, true, new Color(rc.r, rc.g, rc.b, s.rarityBorderAlpha));
            UIKit.Stretch(bg.rectTransform);

            var bar = UIKit.Solid(rt, "Rarity", rc);
            UIKit.Anchor(bar.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 6f), new Vector2(4f, -6f));

            float right = toVault ? 92f : 16f;
            var name = UIKit.Text(rt, "Name", s, ItemText.Name(item), s.bodySize, rarity.HasValue && rarity.Value != Rarity.Common ? rc : s.text);
            UIKit.Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(18f, 0f), new Vector2(-right, -4f));

            string kind = ItemText.Kind(item);
            string sub = rarity.HasValue ? rarity.Value + (string.IsNullOrEmpty(kind) ? "" : "  /  " + kind) : kind;
            var line = UIKit.Text(rt, "Kind", s, sub, s.smallSize - 1f, s.textMuted);
            UIKit.Anchor(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(18f, 4f), new Vector2(-right, 0f));

            if (toVault)
            {
                var tag = UIKit.Box(rt, "Vault", s, new Color(s.accent.r, s.accent.g, s.accent.b, 0.16f), true, s.accent);
                UIKit.Anchor(tag.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-80f, -12f), new Vector2(-12f, 12f));
                var t = UIKit.Text(tag.transform, "Text", s, "Vault", s.labelSize, s.accent, TextAlignmentOptions.Center, heading: true);
                t.characterSpacing = 2f;
                UIKit.Stretch(t.rectTransform);
            }

            return new Row { Rect = rt, Group = group };
        }
    }
}
