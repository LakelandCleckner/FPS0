using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Combat.Armor;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.UI
{
    // Filter chips, sort and search for the vault tab. The search field lives outside the generated content so it keeps focus.
    public class VaultFilterBar : InventoryPanel
    {
        [SerializeField] private float searchWidth = 340f;

        private TMP_InputField search;
        private InventoryScreen owner;

        public bool SearchFocused => search != null && search.isFocused;

        public void Unfocus()
        {
            if (search == null) return;
            search.DeactivateInputField();
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == search.gameObject) es.SetSelectedGameObject(null);
        }

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            owner = screen;
            var s = screen.Style;
            var f = screen.Filter;
            var view = screen.View;
            UIKit.VStack(root, s.gap * 0.8f);

            // Show, sort, clear
            var top = Row(root, s, "Show");
            Chip(top, s, "All", f.Kind == ItemKindFilter.All, null, () => f.Kind = ItemKindFilter.All);
            Chip(top, s, "Weapons", f.Kind == ItemKindFilter.Weapons, null, () => f.Kind = ItemKindFilter.Weapons);
            Chip(top, s, "Armor", f.Kind == ItemKindFilter.Armor, null, () => f.Kind = ItemKindFilter.Armor);
            Gap(top, s);
            UIKit.Label(top, "SortLabel", s, "Sort");
            Chip(top, s, ItemFilter.SortLabel(f.Sort), true, null, f.NextSort);
            Chip(top, s, ItemFilter.DirectionLabel(f.Sort, f.Descending), false, null, () => f.Descending = !f.Descending);
            if (!f.IsDefault)
            {
                Gap(top, s);
                Chip(top, s, "Clear filters", false, s.negative, () =>
                {
                    f.Clear();
                    if (search != null) search.SetTextWithoutNotify("");
                });
            }
            UIKit.Spacer(top);
            UIKit.Size(UIKit.Node(top, "SearchSpace"), width: searchWidth);

            // Rarity
            var rar = Row(root, s, "Rarity");
            foreach (Rarity r in Enum.GetValues(typeof(Rarity)))
            {
                var rr = r;
                Chip(rar, s, r.ToString(), f.Rarities.Contains(r), s.RarityColor(r), () => ItemFilter.Toggle(f.Rarities, rr));
            }

            // Weapons: type, carried slot, element
            if (f.Kind != ItemKindFilter.Armor)
            {
                var types = new List<WeaponTypeSO>();
                var elements = new List<string>();
                Collect(view.CarriedWeapons, types, elements);
                Collect(view.VaultWeapons, types, elements);

                var wr = Row(root, s, "Weapons");
                foreach (var t in types)
                {
                    var tt = t;
                    Chip(wr, s, string.IsNullOrWhiteSpace(t.displayName) ? t.name : t.displayName, f.WeaponTypes.Contains(t), null,
                         () => ItemFilter.Toggle(f.WeaponTypes, tt));
                }
                if (types.Count > 0) Gap(wr, s);
                for (int i = 0; i < view.Weapons.Length; i++)
                {
                    int slot = i;
                    Chip(wr, s, "Slot " + (i + 1), f.WeaponSlots.Contains(i), null, () => ItemFilter.Toggle(f.WeaponSlots, slot));
                }
                if (elements.Count > 0) Gap(wr, s);
                foreach (var e in elements)
                {
                    var ee = e;
                    Chip(wr, s, e, f.Elements.Contains(e), null, () => ItemFilter.Toggle(f.Elements, ee));
                }
            }

            // Armor slots
            if (f.Kind != ItemKindFilter.Weapons && view.ArmorSlots.Count > 0)
            {
                var ar = Row(root, s, "Armor");
                foreach (var slot in view.ArmorSlots)
                {
                    var sl = slot;
                    Chip(ar, s, ItemText.SlotName(slot), f.ArmorSlots.Contains(slot), null, () => ItemFilter.Toggle(f.ArmorSlots, sl));
                }
            }

            EnsureSearch(s, f);
        }

        // ------------------------------------------------------------------ parts

        private static RectTransform Row(Transform parent, UIStyleSO s, string label)
        {
            var row = UIKit.Node(parent, "Row_" + label);
            UIKit.HStack(row, 6f);
            UIKit.Size(row, height: s.chipSize + 6f);
            UIKit.Size(UIKit.Label(row, "Label", s, label, s.textFaint), width: 74f);
            return row;
        }

        private static void Gap(Transform row, UIStyleSO s) => UIKit.Size(UIKit.Node(row, "Gap"), width: s.gap * 1.5f);

        // Toggle chip; active chips take the accent (or the given colour).
        private void Chip(Transform row, UIStyleSO s, string label, bool active, Color? color, Action onClick)
        {
            Color c = color ?? s.accent;
            var fill = UIKit.Box(row, "Chip_" + label, s, active ? new Color(c.r, c.g, c.b, 0.18f) : s.tileEmpty);
            var border = UIKit.Outline(fill.rectTransform, s, active ? c : s.tileBorder);
            UIKit.HStack(fill, 0f, UIKit.Pad(10f, 10f, 3f, 3f), TextAnchor.MiddleCenter);
            UIKit.Size(fill, height: s.chipSize);
            var t = UIKit.Text(fill.transform, "Text", s, label, s.labelSize, active ? (color ?? s.text) : s.textMuted,
                               TextAlignmentOptions.Center, heading: true);
            t.characterSpacing = 1f;

            var tile = fill.gameObject.AddComponent<UITile>();
            tile.Setup(fill, border, null, fill.color, s.tileHover, border.color, s.accent);
            tile.LeftClick += _ =>
            {
                onClick();
                if (owner != null) owner.RequestRebuild();
            };
        }

        private static void Collect(IReadOnlyList<WeaponInstance> weapons, List<WeaponTypeSO> types, List<string> elements)
        {
            foreach (var w in weapons)
            {
                var t = ItemFilter.TypeOf(w);
                if (t != null && !types.Contains(t)) types.Add(t);
                string e = ItemText.Element(w);
                if (!string.IsNullOrEmpty(e) && !elements.Contains(e)) elements.Add(e);
            }
        }

        // ------------------------------------------------------------------ search

        private void EnsureSearch(UIStyleSO s, ItemFilter f)
        {
            if (search == null)
            {
                var found = transform.Find("Search");
                if (found != null) search = found.GetComponent<TMP_InputField>();
            }

            if (search == null)
            {
                var rt = UIKit.Node(transform, "Search");
                rt.gameObject.SetActive(false);
                var bg = rt.gameObject.AddComponent<Image>();
                bg.sprite = s.Fill;
                bg.type = Image.Type.Sliced;

                var area = UIKit.Stretch(UIKit.Node(rt, "TextArea"), 12f, 2f, 12f, 2f);
                area.gameObject.AddComponent<RectMask2D>();
                var placeholder = UIKit.Text(area, "Placeholder", s, "Search names and perks", s.smallSize, s.textFaint);
                UIKit.Stretch(placeholder.rectTransform);
                var text = UIKit.Text(area, "Text", s, "", s.smallSize, s.text);
                text.overflowMode = TextOverflowModes.Overflow;
                UIKit.Stretch(text.rectTransform);

                search = rt.gameObject.AddComponent<TMP_InputField>();
                search.textViewport = area;
                search.textComponent = text;
                search.placeholder = placeholder;
                search.targetGraphic = bg;
                search.lineType = TMP_InputField.LineType.SingleLine;
                search.navigation = new Navigation { mode = Navigation.Mode.None };
                rt.gameObject.SetActive(true);
            }

            var srt = (RectTransform)search.transform;
            srt.SetAsLastSibling();
            float h = s.chipSize + 6f;
            UIKit.Anchor(srt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-searchWidth, -h), Vector2.zero);
            if (search.targetGraphic != null) search.targetGraphic.color = s.tile;

            search.onValueChanged.RemoveListener(OnSearch);
            search.onValueChanged.AddListener(OnSearch);
            if (search.text != f.Search) search.SetTextWithoutNotify(f.Search);
        }

        private void OnSearch(string value)
        {
            if (owner == null) return;
            owner.Filter.Search = value;
            owner.RequestRebuild();
        }
    }
}
