using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Combat.Armor;
using Combat.Info;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.UI
{
    // Equipped weapons and armor. Click to choose a slot, right-click to unequip it.
    // Cards stay between refreshes; only a slot whose item changed is rebuilt, with a flash.
    public class LoadoutSlotsPanel : InventoryPanel
    {
        private readonly List<UITile> weaponCards = new List<UITile>();
        private readonly List<UITile> armorCards = new List<UITile>();
        private readonly List<object> weaponItems = new List<object>();
        private readonly List<object> armorItems = new List<object>();
        private RectTransform body;

        protected override bool CanRefresh => body != null;

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            var view = screen.View;
            weaponCards.Clear(); armorCards.Clear(); weaponItems.Clear(); armorItems.Clear();
            Surface(root, s);

            var area = UIKit.Stretch(UIKit.Node(root, "Area"));
            body = UIKit.Scroll(area, s.gap, UIKit.Pad(s.padding));

            Heading(body, s, "Weapons");
            for (int i = 0; i < view.Weapons.Length; i++)
            {
                weaponCards.Add(WeaponCard(body, screen, i, view.Weapons[i]));
                weaponItems.Add(view.Weapons[i]);
            }

            var gap = UIKit.Node(body, "Gap");
            UIKit.Size(gap, height: s.sectionGap - s.gap);

            Heading(body, s, "Armor");
            for (int i = 0; i < view.ArmorSlots.Count; i++)
            {
                armorCards.Add(ArmorCard(body, screen, i, view.ArmorSlots[i], view.Armor[i]));
                armorItems.Add(view.Armor[i]);
            }
        }

        protected override void Refresh(InventoryScreen screen, RectTransform root)
        {
            var view = screen.View;
            if (weaponCards.Count != view.Weapons.Length || armorCards.Count != view.ArmorSlots.Count)
            {
                body = null;
                Rebuild(screen);
                return;
            }

            for (int i = 0; i < weaponCards.Count; i++)
            {
                if (weaponItems[i] != view.Weapons[i])
                {
                    weaponCards[i] = Replace(weaponCards[i], WeaponCard(body, screen, i, view.Weapons[i]), screen);
                    weaponItems[i] = view.Weapons[i];
                }
                weaponCards[i].SetSelected(screen.Selected.Is(SlotKind.Weapon, i));
            }
            for (int i = 0; i < armorCards.Count; i++)
            {
                if (armorItems[i] != view.Armor[i])
                {
                    armorCards[i] = Replace(armorCards[i], ArmorCard(body, screen, i, view.ArmorSlots[i], view.Armor[i]), screen);
                    armorItems[i] = view.Armor[i];
                }
                armorCards[i].SetSelected(screen.Selected.Is(SlotKind.Armor, i));
            }
        }

        // New card takes the old one's place in the list, then flashes.
        private static UITile Replace(UITile old, UITile fresh, InventoryScreen screen)
        {
            int index = old.transform.GetSiblingIndex();
            old.gameObject.SetActive(false);
            UIKit.Destroy(old.gameObject);
            fresh.transform.SetSiblingIndex(index);
            var s = screen.Style;
            UIFlash.Play((RectTransform)fresh.transform, s, new Color(s.accent.r, s.accent.g, s.accent.b, 0.45f));
            return fresh;
        }

        private static UITile WeaponCard(Transform parent, InventoryScreen screen, int slot, WeaponInstance weapon)
        {
            var s = screen.Style;
            var key = new SlotRef(SlotKind.Weapon, slot);
            var tile = Tile(parent, "Weapon_" + (slot + 1), screen, weapon, wide: true);
            UIKit.Size(tile, height: s.weaponSlotHeight);
            UIKit.HStack(tile, 10f, UIKit.Pad(12f, 12f, 8f, 8f));
            tile.SetSelected(screen.Selected.Is(SlotKind.Weapon, slot));
            tile.LeftClick += _ => screen.Select(key);
            tile.RightClick += _ => screen.Unequip(key);
            tile.HoverChanged += (_, on) => screen.HoverSlot(key, on);
            DragTargets(tile, screen, weapon, key);

            var num = UIKit.Text(tile.transform, "Slot", s, (slot + 1).ToString(), s.headingSize + 4f, s.textFaint, TextAlignmentOptions.Center, heading: true);
            UIKit.Size(num, width: 16f);

            Icon(tile.transform, s, weapon, s.weaponSlotHeight - 16f);

            var info = UIKit.Node(tile.transform, "Info");
            UIKit.VStack(info, 1f, null, TextAnchor.MiddleLeft);
            UIKit.Size(info, flexWidth: 1f);

            if (weapon == null)
            {
                UIKit.Text(info, "Name", s, "Empty", s.bodySize, s.textFaint);
                UIKit.Text(info, "Kind", s, "Drop or click a carried weapon", s.smallSize, s.textFaint);
                return tile;
            }

            UIKit.Text(info, "Name", s, ItemText.Name(weapon), s.bodySize, ItemText.NameColor(s, weapon, s.text), role: TextRole.Title);
            string kind = ItemText.Kind(weapon);
            string element = ItemText.Element(weapon);
            var type = ItemText.ElementType(weapon);
            var kindLine = UIKit.Node(info, "Kind");
            UIKit.HStack(kindLine, 5f);
            UIKit.Text(kindLine, "Text", s, string.IsNullOrEmpty(element) ? kind : kind + "  /", s.smallSize, s.textMuted);
            if (type != null)
            {
                UIKit.Icon(kindLine, "Icon", type.icon, type.color, s.smallSize + 4f);
                UIKit.Text(kindLine, "Element", s, element, s.smallSize, type.color);
            }
            UIKit.Spacer(kindLine);
            ChipGrid(tile.transform, screen, weapon.Definition.ResolvedIntrinsics, weapon.Perks, screen.ContextFor(weapon), s.weaponSlotHeight - 16f);
            return tile;
        }

        private static UITile ArmorCard(Transform parent, InventoryScreen screen, int index, ArmorSlotSO slot, ArmorInstance piece)
        {
            var s = screen.Style;
            var key = new SlotRef(SlotKind.Armor, index);
            var tile = Tile(parent, "Armor_" + ItemText.SlotName(slot), screen, piece, wide: true);
            UIKit.Size(tile, height: s.armorSlotHeight);
            UIKit.HStack(tile, 10f, UIKit.Pad(12f, 12f, 6f, 6f));
            tile.SetSelected(screen.Selected.Is(SlotKind.Armor, index));
            tile.LeftClick += _ => screen.Select(key);
            tile.RightClick += _ => screen.Unequip(key);
            tile.HoverChanged += (_, on) => screen.HoverSlot(key, on);
            DragTargets(tile, screen, piece, key);

            Icon(tile.transform, s, piece, s.armorSlotHeight - 12f);

            var info = UIKit.Node(tile.transform, "Info");
            UIKit.VStack(info, 0f, null, TextAnchor.MiddleLeft);
            UIKit.Size(info, flexWidth: 1f);
            UIKit.Text(info, "Slot", s, ItemText.SlotName(slot), s.labelSize + 1f, s.textMuted, TextAlignmentOptions.MidlineLeft, heading: true);
            UIKit.Text(info, "Name", s, piece != null ? ItemText.Name(piece) : "Empty", s.smallSize + 2f,
                       piece != null ? ItemText.NameColor(s, piece, s.text) : s.textFaint,
                       role: piece != null ? TextRole.Title : TextRole.Body);

            if (piece != null) ChipGrid(tile.transform, screen, piece.Definition.intrinsicPerks, piece.Perks, screen.ContextFor(piece), s.armorSlotHeight - 12f);
            return tile;
        }

        // A card can be dragged out to unequip, and takes carried items dropped on it.
        private static void DragTargets(UITile tile, InventoryScreen screen, object item, SlotRef key)
        {
            if (item != null) tile.gameObject.AddComponent<DragItem>().Setup(screen, item, key);
            DropTarget.Attach((RectTransform)tile.transform, screen, DropKind.Slot, key);
        }

        // Square item icon with its rarity wash; the monogram stands in until icon art exists.
        public static void Icon(Transform parent, UIStyleSO s, object item, float size)
        {
            var box = UIKit.Box(parent, "Icon", s, item != null ? s.backdrop : s.tileEmpty);
            UIKit.Size(box, width: size, height: size);
            if (item != null) UIKit.RarityWash(box.rectTransform, s, ItemText.RarityOf(item));
            UIKit.Outline(box.rectTransform, s, ItemText.BorderColor(s, item));
            if (item == null) return;
            var t = UIKit.Text(box.transform, "Monogram", s, ItemText.Monogram(item), size * 0.34f, s.text, TextAlignmentOptions.Center, role: TextRole.Title);
            UIKit.Stretch(t.rectTransform);
            var type = item is WeaponInstance w ? ItemText.ElementType(w) : null;
            if (type != null) UIKit.Badge(box.rectTransform, "Element", type.icon, type.color, size * 0.28f, new Vector2(1f, 0f), 6f);
        }

        // Perk chips on the card's right, intrinsics first with an accent border, wrapping every Chips Per Row.
        // At most two rows, sized to fit the card's inner height; more chips widen the grid instead of overflowing.
        private static void ChipGrid(Transform parent, InventoryScreen screen, IReadOnlyList<PerkSO> intrinsic, IReadOnlyList<PerkSO> perks, DescribeContext ctx, float maxHeight)
        {
            var s = screen.Style;
            int count = 0;
            if (intrinsic != null) foreach (var p in intrinsic) if (p != null) count++;
            foreach (var p in perks) if (p != null) count++;
            if (count == 0) return;

            const float gap = 3f;
            int perRow = Mathf.Max(1, s.chipsPerRow);
            int rows = Mathf.Min(2, Mathf.CeilToInt(count / (float)perRow));
            perRow = Mathf.CeilToInt(count / (float)rows);
            float size = Mathf.Min(s.cardChipSize, (maxHeight - gap * (rows - 1)) / rows);

            var grid = UIKit.Node(parent, "Perks");
            var g = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            g.cellSize = new Vector2(size, size);
            g.spacing = new Vector2(gap, gap);
            g.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = perRow;
            g.childAlignment = TextAnchor.MiddleRight;
            UIKit.Size(grid, width: perRow * size + (perRow - 1) * gap, height: rows * size + (rows - 1) * gap);
            if (intrinsic != null) foreach (var p in intrinsic) if (p != null) Chip(grid, s, p, screen, ctx, size, true);
            foreach (var p in perks) if (p != null) Chip(grid, s, p, screen, ctx, size);
        }

        // Perk chip; with a screen it shows the perk's name and description on hover.
        public static void Chip(Transform parent, UIStyleSO s, PerkSO perk, InventoryScreen screen = null, DescribeContext ctx = null, float size = -1f, bool intrinsic = false)
        {
            if (size <= 0f) size = s.chipSize;
            // Elemental perks wear their element's colour; other intrinsics the accent
            bool elemental = ItemText.PerkElement(perk, out var type);
            Color tint = elemental ? type.color : s.accent;
            var chip = UIKit.Box(parent, "Chip_" + perk.name, s, s.tileEmpty, true, intrinsic || elemental ? tint : (Color?)null, small: true);
            string title = intrinsic ? ItemText.PerkName(perk) + "  (Intrinsic)" : ItemText.PerkName(perk);
            if (screen != null) InfoHover.Attach(chip, screen, title, tint, perk, ctx ?? screen.ContextFor(null));
            UIKit.Size(chip, width: size, height: size);
            if (perk.icon != null)
            {
                var img = UIKit.Node(chip.transform, "Icon").gameObject.AddComponent<UnityEngine.UI.Image>();
                img.sprite = perk.icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
                UIKit.Stretch(img.rectTransform, 3f, 3f, 3f, 3f);
            }
            else
            {
                var t = UIKit.Text(chip.transform, "Label", s, perk.ShortLabel, s.labelSize - 1f, intrinsic || elemental ? tint : s.textMuted, TextAlignmentOptions.Center, heading: true);
                t.characterSpacing = 0f;
                UIKit.Stretch(t.rectTransform);
            }
            if (elemental) UIKit.Badge(chip.rectTransform, "Element", type.icon, tint, size * 0.36f, new Vector2(1f, 0f), 1f);
        }
    }
}
