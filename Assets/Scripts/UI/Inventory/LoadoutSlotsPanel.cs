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

            var body = UIKit.Node(root, "Body");
            UIKit.Stretch(body);
            UIKit.VStack(body, s.gap, UIKit.Pad(s.padding));

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
                UIKit.Text(info, "Kind", s, "Pick a carried weapon to equip", s.smallSize, s.textFaint);
                return;
            }

            UIKit.Text(info, "Name", s, ItemText.Name(weapon), s.bodySize + 1f, ItemText.NameColor(s, weapon, s.text));
            string kind = ItemText.Kind(weapon);
            string element = ItemText.Element(weapon);
            UIKit.Text(info, "Kind", s, string.IsNullOrEmpty(element) ? kind : kind + "  /  " + element, s.smallSize, s.textMuted);
            Chips(info, s, weapon.Perks);
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
            UIKit.Text(info, "Name", s, piece != null ? ItemText.Name(piece) : "Empty", s.bodySize - 1f, piece != null ? ItemText.NameColor(s, piece, s.text) : s.textFaint);

            if (piece != null)
            {
                var chips = UIKit.Node(tile.transform, "Perks");
                UIKit.HStack(chips, 4f, null, TextAnchor.MiddleRight);
                foreach (var p in piece.Perks) if (p != null) Chip(chips, s, p);
            }
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

        private static void Chips(Transform parent, UIStyleSO s, System.Collections.Generic.IReadOnlyList<PerkSO> perks)
        {
            var row = UIKit.Node(parent, "Perks");
            UIKit.HStack(row, 4f);
            bool any = false;
            foreach (var p in perks) if (p != null) { Chip(row, s, p); any = true; }
            if (!any) UIKit.Size(row, height: 0f);
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
