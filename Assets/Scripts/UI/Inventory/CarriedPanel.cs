using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Combat.Weapons;

namespace Combat.UI
{
    // Carried items that fit the selected slot, with that slot's carried cap. Click one to equip it.
    public class CarriedPanel : InventoryPanel
    {
        private readonly List<object> items = new List<object>();

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            var view = screen.View;
            var slot = screen.Selected;
            Surface(root, s);

            view.CarriedFor(slot, items);
            int own = view.CarriedCount(slot);
            int cap = view.Cap(slot);
            bool full = own >= cap;

            var body = UIKit.Stretch(UIKit.Node(root, "Body"));
            UIKit.VStack(body, s.gap, UIKit.Pad(s.padding));

            string slotName = slot.Kind == SlotKind.Weapon ? "Slot " + (slot.Index + 1)
                : slot.Index < view.ArmorSlots.Count ? ItemText.SlotName(view.ArmorSlots[slot.Index]) : "";
            Heading(body, s, "Carried", own + " / " + cap, full ? s.negative : s.textMuted);
            UIKit.Text(body, "Hint", s, "Equip into " + slotName, s.smallSize, s.textMuted);

            var area = UIKit.Node(body, "Area");
            UIKit.Size(area, flexHeight: 1f);
            var content = UIKit.Scroll(area, 0f);

            int columns = Mathf.Max(1, s.gridColumns);
            int empties = Mathf.Max(0, cap - own);
            int total = items.Count + empties;
            int rows = Mathf.Max(1, Mathf.CeilToInt(total / (float)columns));

            var grid = UIKit.Node(content, "Grid");
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(s.gridTileSize, s.gridTileSize);
            g.spacing = new Vector2(s.gap, s.gap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.childAlignment = TextAnchor.UpperLeft;
            UIKit.Size(grid, height: rows * s.gridTileSize + (rows - 1) * s.gap);

            foreach (var item in items) ItemTile(grid, screen, item, slot);
            for (int i = 0; i < empties; i++) EmptyTile(grid, s);
        }

        private static void ItemTile(Transform grid, InventoryScreen screen, object item, SlotRef slot)
        {
            var s = screen.Style;
            var tile = ItemTile(grid, screen, item);
            tile.LeftClick += t => screen.EquipToSelected(t.Item);

            // Counts against another slot's cap: say which.
            if (item is WeaponInstance w && slot.Kind == SlotKind.Weapon && w.Definition.InventorySlot != slot.Index)
            {
                var tag = UIKit.Box(tile.transform, "Tag", s, s.tileEmpty, true);
                UIKit.Anchor(tag.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -26f), new Vector2(-6f, -6f));
                var t = UIKit.Text(tag.transform, "Text", s, "S" + (w.Definition.InventorySlot + 1), s.labelSize - 1f, s.textMuted,
                                   TextAlignmentOptions.Center, heading: true);
                t.characterSpacing = 0f;
                UIKit.Stretch(t.rectTransform);
            }
        }

        private static void EmptyTile(Transform grid, UIStyleSO s)
        {
            UIKit.Box(grid, "Empty", s, s.tileEmpty, true, new Color(s.tileBorder.r, s.tileBorder.g, s.tileBorder.b, s.tileBorder.a * 0.6f));
        }
    }
}
