using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Combat.UI
{
    // Everything carried, in a section per carried slot with its cap; the selected slot's section first.
    // Click an item to equip it. Tiles persist between refreshes and slide to their new places.
    public class CarriedPanel : InventoryPanel
    {
        private sealed class Section
        {
            public RectTransform Row;
            public TMP_Text Title, Count;
        }

        private readonly List<object> items = new List<object>();
        private readonly List<SlotRef> order = new List<SlotRef>();
        private readonly Dictionary<long, Section> sections = new Dictionary<long, Section>();
        private SlideLayout layout;
        private TMP_Text hint;

        protected override bool CanRefresh => layout != null;

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            sections.Clear();
            Surface(root, s);

            var body = UIKit.Stretch(UIKit.Node(root, "Body"));
            UIKit.VStack(body, s.gap, UIKit.Pad(s.padding));
            Heading(body, s, "Carried");
            hint = UIKit.Text(body, "Hint", s, "", s.smallSize, s.textMuted);

            var area = UIKit.Node(body, "Area");
            UIKit.Size(area, flexHeight: 1f);
            layout = UIKit.SlideScroll(area);
            DropTarget.Attach(root, screen, DropKind.LoadoutCarried);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            Refresh(screen, root);
        }

        protected override void Refresh(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            var view = screen.View;
            hint.text = view.CarriedWeapons.Count + view.CarriedArmor.Count == 0 ? "Nothing carried." : "Click an item to equip it";

            // The scroll area's width, or the panel's when the area isn't laid out yet.
            float width = layout.Width >= s.gridTileSize ? layout.Width : root.rect.width - 2f * s.padding;
            int columns = width > 0f ? Mathf.Max(1, Mathf.FloorToInt((width + s.gap) / (s.gridTileSize + s.gap))) : Mathf.Max(1, s.gridColumns);
            float headHeight = s.headingSize + 12f;

            Order(screen);
            layout.Begin();
            float y = 0f;
            foreach (var slot in order)
            {
                var sec = SectionFor(screen, slot);
                bool selected = slot.Kind == screen.Selected.Kind && slot.Index == screen.Selected.Index;
                int own = view.CarriedCount(slot), cap = view.Cap(slot);
                sec.Title.color = selected ? s.accent : s.text;
                sec.Count.text = own + " / " + cap;
                sec.Count.color = own >= cap ? s.negative : s.textMuted;
                layout.Place(Key(slot), sec.Row, new Vector2(0f, -y), new Vector2(width, headHeight));
                y += headHeight + s.gap * 0.6f;

                view.CarriedIn(slot, items);
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var rect = layout.Get(item);
                    if (rect == null)
                    {
                        var tile = ItemTile(layout.transform, screen, item);
                        tile.LeftClick += t => screen.EquipAuto(t.Item);
                        rect = (RectTransform)tile.transform;
                    }
                    int col = i % columns, row = i / columns;
                    layout.Place(item, rect, new Vector2(col * (s.gridTileSize + s.gap), -(y + row * (s.gridTileSize + s.gap))),
                                 new Vector2(s.gridTileSize, s.gridTileSize));
                }
                int rows = Mathf.CeilToInt(items.Count / (float)columns);
                y += rows * (s.gridTileSize + s.gap) + (rows > 0 ? s.sectionGap - s.gap : s.gap);
            }
            layout.End(y, screen.LastDeleted, new Color(s.negative.r, s.negative.g, s.negative.b, 0.55f));
        }

        // Selected slot first, then weapon slots, then armor slots.
        private void Order(InventoryScreen screen)
        {
            var view = screen.View;
            order.Clear();
            var sel = screen.Selected;
            bool selValid = sel.Kind == SlotKind.Weapon ? sel.Index < view.Weapons.Length : sel.Index < view.ArmorSlots.Count;
            if (selValid) order.Add(sel);
            for (int i = 0; i < view.Weapons.Length; i++)
                if (!(selValid && sel.Is(SlotKind.Weapon, i))) order.Add(new SlotRef(SlotKind.Weapon, i));
            for (int i = 0; i < view.ArmorSlots.Count; i++)
                if (!(selValid && sel.Is(SlotKind.Armor, i))) order.Add(new SlotRef(SlotKind.Armor, i));
        }

        private Section SectionFor(InventoryScreen screen, SlotRef slot)
        {
            long key = Key(slot);
            if (sections.TryGetValue(key, out var sec) && sec.Row != null) return sec;

            var s = screen.Style;
            var view = screen.View;
            string name = slot.Kind == SlotKind.Weapon ? "Slot " + (slot.Index + 1) : ItemText.SlotName(view.ArmorSlots[slot.Index]);
            var row = UIKit.Node(layout.transform, "Section_" + name);
            UIKit.HStack(row, 12f);
            sec = new Section { Row = row };
            sec.Title = UIKit.Text(row, "Title", s, name, s.headingSize, s.text, heading: true);
            var rule = UIKit.Solid(row, "Rule", s.divider);
            UIKit.Size(rule, height: 1f, flexWidth: 1f);
            sec.Count = UIKit.Text(row, "Count", s, "", s.labelSize + 1f, s.textMuted, TextAlignmentOptions.MidlineRight, role: TextRole.Number);
            sections[key] = sec;
            return sec;
        }

        private static long Key(SlotRef slot) => ((long)slot.Kind << 32) | (uint)slot.Index;
    }
}
