using TMPro;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.UI
{
    // Equipped weapons and armor. Click to choose a slot, right-click to unequip it.
    public class LoadoutSlotsPanel : InventoryPanel
    {
        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            var view = screen.View;
            Surface(root, s);

            var area = UIKit.Stretch(UIKit.Node(root, "Area"));
            var body = UIKit.Scroll(area, s.gap, UIKit.Pad(s.padding));

            Heading(body, s, "Weapons");
            for (int i = 0; i < view.Weapons.Length; i++) WeaponCard(body, screen, i, view.Weapons[i]);

            var gap = UIKit.Node(body, "Gap");
            UIKit.Size(gap, height: s.sectionGap - s.gap);

            Heading(body, s, "Armor");
            for (int i = 0; i < view.ArmorSlots.Count; i++) ArmorCard(body, screen, i, view.ArmorSlots[i], view.Armor[i]);
        }

        private static void WeaponCard(Transform parent, InventoryScreen screen, int slot, WeaponInstance weapon)
        {
            var s = screen.Style;
            var key = new SlotRef(SlotKind.Weapon, slot);
            var tile = Tile(parent, "Weapon_" + (slot + 1), screen, weapon);
            UIKit.Size(tile, height: s.weaponSlotHeight);
            UIKit.HStack(tile, 14f, UIKit.Pad(16f, 16f, 12f, 12f));
            tile.SetSelected(screen.Selected.Is(SlotKind.Weapon, slot));
            tile.LeftClick += _ => screen.Select(key);
            tile.RightClick += _ => screen.Unequip(key);

            var num = UIKit.Text(tile.transform, "Slot", s, (slot + 1).ToString(), s.titleSize * 0.8f, s.textFaint, TextAlignmentOptions.Center, heading: true);
            UIKit.Size(num, width: 28f);

            Icon(tile.transform, s, weapon, s.weaponSlotHeight - 28f);

            var info = UIKit.Node(tile.transform, "Info");
            UIKit.VStack(info, 4f, null, TextAnchor.MiddleLeft);
            UIKit.Size(info, flexWidth: 1f);

            if (weapon == null)
            {
                UIKit.Text(info, "Name", s, "Empty", s.bodySize, s.textFaint);
                UIKit.Text(info, "Kind", s, "Pick a carried weapon to equip", s.smallSize, s.textFaint, lines: 2);
                return;
            }

            UIKit.Text(info, "Name", s, ItemText.Name(weapon), s.bodySize + 1f, ItemText.NameColor(s, weapon, s.text), lines: 2);
            string kind = ItemText.Kind(weapon);
            string element = ItemText.Element(weapon);
            UIKit.Text(info, "Kind", s, string.IsNullOrEmpty(element) ? kind : kind + "  /  " + element, s.smallSize, s.textMuted, lines: 2);
            ChipGrid(tile.transform, s, weapon.Perks);
        }

        private static void ArmorCard(Transform parent, InventoryScreen screen, int index, ArmorSlotSO slot, ArmorInstance piece)
        {
            var s = screen.Style;
            var key = new SlotRef(SlotKind.Armor, index);
            var tile = Tile(parent, "Armor_" + ItemText.SlotName(slot), screen, piece);
            UIKit.Size(tile, height: s.armorSlotHeight);
            UIKit.HStack(tile, 14f, UIKit.Pad(16f, 16f, 8f, 8f));
            tile.SetSelected(screen.Selected.Is(SlotKind.Armor, index));
            tile.LeftClick += _ => screen.Select(key);
            tile.RightClick += _ => screen.Unequip(key);

            Icon(tile.transform, s, piece, s.armorSlotHeight - 18f);

            var info = UIKit.Node(tile.transform, "Info");
            UIKit.VStack(info, 2f, null, TextAnchor.MiddleLeft);
            UIKit.Size(info, flexWidth: 1f);
            UIKit.Label(info, "Slot", s, ItemText.SlotName(slot));
            UIKit.Text(info, "Name", s, piece != null ? ItemText.Name(piece) : "Empty", s.bodySize - 1f,
                       piece != null ? ItemText.NameColor(s, piece, s.text) : s.textFaint, lines: piece != null ? 2 : 1);

            if (piece != null) ChipGrid(tile.transform, s, piece.Perks);
        }

        // Square item icon; the monogram stands in until icon art exists.
        public static void Icon(Transform parent, UIStyleSO s, object item, float size)
        {
            var box = UIKit.Box(parent, "Icon", s, item != null ? s.tileHover : s.tileEmpty, true);
            UIKit.Size(box, width: size, height: size);
            if (item == null) return;
            var t = UIKit.Text(box.transform, "Monogram", s, ItemText.Monogram(item), size * 0.32f, s.text, TextAlignmentOptions.Center, heading: true);
            UIKit.Stretch(t.rectTransform);
        }

        // Perk chips on the card's right, wrapping to a new row every Chips Per Row.
        private static void ChipGrid(Transform parent, UIStyleSO s, System.Collections.Generic.IReadOnlyList<PerkSO> perks)
        {
            int count = 0;
            foreach (var p in perks) if (p != null) count++;
            if (count == 0) return;

            const float gap = 4f;
            int perRow = Mathf.Max(1, s.chipsPerRow);
            int rows = Mathf.CeilToInt(count / (float)perRow);

            var grid = UIKit.Node(parent, "Perks");
            var g = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            g.cellSize = new Vector2(s.chipSize, s.chipSize);
            g.spacing = new Vector2(gap, gap);
            g.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = perRow;
            g.childAlignment = TextAnchor.MiddleRight;
            UIKit.Size(grid, width: perRow * s.chipSize + (perRow - 1) * gap, height: rows * s.chipSize + (rows - 1) * gap);
            foreach (var p in perks) if (p != null) Chip(grid, s, p);
        }

        public static void Chip(Transform parent, UIStyleSO s, PerkSO perk)
        {
            var chip = UIKit.Box(parent, "Chip_" + perk.name, s, s.tileEmpty, true);
            UIKit.Size(chip, width: s.chipSize, height: s.chipSize);
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
                var t = UIKit.Text(chip.transform, "Label", s, perk.ShortLabel, s.labelSize - 1f, s.textMuted, TextAlignmentOptions.Center, heading: true);
                t.characterSpacing = 0f;
                UIKit.Stretch(t.rectTransform);
            }
        }
    }
}
