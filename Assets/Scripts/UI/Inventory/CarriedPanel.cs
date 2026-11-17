using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Combat.UI
{
    // Everything carried, in a collapsible section per carried slot with its cap, in a fixed order; the selected slot's title is lit.
    // Sections can be pinned open or pinned closed; otherwise, with auto-open the hovered or selected slot's section opens and the rest close. Click an item to equip it.
    public class CarriedPanel : InventoryPanel
    {
        private sealed class Section
        {
            public RectTransform Row;
            public TMP_Text Arrow, Title, Count, Pin;
            public bool Hover;
        }

        private const string PinPrefs = "FPS0.CarriedPins";
        private enum PinState { None, Open, Closed }
        private static Dictionary<long, PinState> pins;

        private readonly HashSet<long> opened = new HashSet<long>();
        private readonly HashSet<long> shut = new HashSet<long>();
        private readonly Dictionary<long, int> lastCounts = new Dictionary<long, int>();
        private long lastSelected = long.MinValue, recent = long.MinValue;
        private InventoryScreen owner;

        private readonly List<object> items = new List<object>();
        private readonly List<SlotRef> order = new List<SlotRef>();
        private readonly Dictionary<long, Section> sections = new Dictionary<long, Section>();
        private SlideLayout layout;
        private TMP_Text hint;

        protected override bool CanRefresh => layout != null;

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            owner = screen;
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
            hint.text = view.CarriedWeapons.Count + view.CarriedArmor.Count == 0 ? "Nothing carried." : "Click an item to equip it, a header to open it";

            // The scroll area's width, or the panel's when the area isn't laid out yet.
            float width = layout.Width >= s.gridTileSize ? layout.Width : root.rect.width - 2f * s.padding;
            // Fixed columns; tiles size to fill the width.
            int columns = Mathf.Max(1, s.carriedColumns);
            float cell = Mathf.Max(24f, (width - s.gap * (columns - 1)) / columns);
            float headHeight = s.headingSize + 16f;

            Order(screen);
            Track(screen);
            layout.Begin();
            float y = 0f;
            foreach (var slot in order)
            {
                long key = Key(slot);
                var sec = SectionFor(screen, slot);
                bool open = IsOpen(screen, key);
                int own = view.CarriedCount(slot), cap = view.Cap(slot);
                Paint(screen, key, sec);
                sec.Count.text = own + " / " + cap;
                sec.Count.color = own >= cap ? s.negative : s.textMuted;
                layout.Place(key, sec.Row, new Vector2(0f, -y), new Vector2(width, headHeight));
                y += headHeight + s.gap * 0.5f;
                if (!open) continue;

                view.CarriedIn(slot, items);
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var rect = layout.Get(item);
                    if (rect == null)
                    {
                        var tile = ItemTile(layout.transform, screen, item, cell);
                        tile.LeftClick += t => screen.EquipAuto(t.Item);
                        rect = (RectTransform)tile.transform;
                    }
                    int col = i % columns, row = i / columns;
                    layout.Place(item, rect, new Vector2(col * (cell + s.gap), -(y + row * (cell + s.gap))), new Vector2(cell, cell));
                }
                int rows = Mathf.CeilToInt(items.Count / (float)columns);
                y += rows * (cell + s.gap) + (rows > 0 ? s.sectionGap - s.gap : s.gap * 0.5f);
            }
            layout.End(y, screen.LastDeleted, new Color(s.negative.r, s.negative.g, s.negative.b, 0.55f));
        }

        // Saved as "key:state" pairs; a bare key is an open pin.
        private static Dictionary<long, PinState> Pins
        {
            get
            {
                if (pins != null) return pins;
                pins = new Dictionary<long, PinState>();
                foreach (var part in PlayerPrefs.GetString(PinPrefs, "").Split(','))
                {
                    var bits = part.Split(':');
                    if (!long.TryParse(bits[0], out long k)) continue;
                    pins[k] = bits.Length > 1 && bits[1] == "2" ? PinState.Closed : PinState.Open;
                }
                return pins;
            }
        }

        private static PinState PinOf(long key) => Pins.TryGetValue(key, out var p) ? p : PinState.None;

        private static void SavePins()
        {
            var parts = new List<string>();
            foreach (var kv in Pins) parts.Add(kv.Key + ":" + (int)kv.Value);
            PlayerPrefs.SetString(PinPrefs, string.Join(",", parts));
        }

        private void Paint(InventoryScreen screen, long key, Section sec)
        {
            var s = screen.Style;
            bool selected = key == Key(screen.Selected), open = IsOpen(screen, key);
            sec.Title.color = selected ? s.accent : (open || sec.Hover ? s.text : s.textMuted);
            sec.Arrow.text = open ? "-" : "+";
            sec.Arrow.color = selected || sec.Hover ? s.accent : s.textFaint;
            var pin = PinOf(key);
            sec.Pin.text = pin == PinState.Open ? "OPEN" : pin == PinState.Closed ? "HIDE" : "PIN";
            sec.Pin.color = pin == PinState.Open ? s.accent : pin == PinState.Closed ? s.negative : s.textFaint;
        }

        private bool IsOpen(InventoryScreen screen, long key)
        {
            var pin = PinOf(key);
            if (pin != PinState.None) return pin == PinState.Open;
            if (shut.Contains(key)) return false;
            if (opened.Contains(key) || key == recent) return true;
            // Auto follows the hovered equipped slot, else the selected one.
            return !screen.Style.carriedAutoOpen || key == Key(screen.HoveredSlot ?? screen.Selected);
        }

        // A new selection resets manual opens; a section that just gained an item opens until then.
        private void Track(InventoryScreen screen)
        {
            long sel = Key(screen.Selected);
            if (sel != lastSelected)
            {
                lastSelected = sel;
                opened.Clear();
                shut.Clear();
                recent = long.MinValue;
            }

            bool first = lastCounts.Count == 0;
            foreach (var slot in order)
            {
                long key = Key(slot);
                int n = screen.View.CarriedCount(slot);
                if (!first && lastCounts.TryGetValue(key, out int was) && n > was) recent = key;
                lastCounts[key] = n;
            }
        }

        private void Toggle(long key)
        {
            if (owner == null || PinOf(key) != PinState.None) return;
            if (IsOpen(owner, key)) { opened.Remove(key); shut.Add(key); if (key == recent) recent = long.MinValue; }
            else { shut.Remove(key); opened.Add(key); }
            owner.RequestRebuild();
        }

        private void TogglePin(long key)
        {
            // None -> pinned open -> pinned closed -> None.
            var next = (PinState)(((int)PinOf(key) + 1) % 3);
            if (next == PinState.None) Pins.Remove(key); else Pins[key] = next;
            opened.Remove(key);
            shut.Remove(key);
            SavePins();
            if (owner != null) owner.RequestRebuild();
        }

        // Weapon slots, then armor slots. Fixed, so sections never jump around.
        private void Order(InventoryScreen screen)
        {
            var view = screen.View;
            order.Clear();
            for (int i = 0; i < view.Weapons.Length; i++) order.Add(new SlotRef(SlotKind.Weapon, i));
            for (int i = 0; i < view.ArmorSlots.Count; i++) order.Add(new SlotRef(SlotKind.Armor, i));
        }

        private Section SectionFor(InventoryScreen screen, SlotRef slot)
        {
            long key = Key(slot);
            if (sections.TryGetValue(key, out var sec) && sec.Row != null) return sec;

            var s = screen.Style;
            var view = screen.View;
            string name = slot.Kind == SlotKind.Weapon ? "Slot " + (slot.Index + 1) : ItemText.SlotName(view.ArmorSlots[slot.Index]);
            var row = UIKit.Node(layout.transform, "Section_" + name);
            UIKit.HStack(row, 10f);
            row.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.clear;
            sec = new Section { Row = row };
            sec.Arrow = UIKit.Text(row, "Arrow", s, "+", s.headingSize + 2f, s.textFaint, TextAlignmentOptions.Center, heading: true);
            UIKit.Size(sec.Arrow, width: 12f);
            sec.Title = UIKit.Text(row, "Title", s, name, s.headingSize, s.text, heading: true);
            var rule = UIKit.Solid(row, "Rule", s.divider);
            UIKit.Size(rule, height: 1f, flexWidth: 1f);
            sec.Count = UIKit.Text(row, "Count", s, "", s.labelSize + 1f, s.textMuted, TextAlignmentOptions.MidlineRight, role: TextRole.Number);

            var pin = UIKit.Node(row, "Pin");
            UIKit.Size(pin, width: 40f, flexHeight: 1f);
            pin.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.clear;
            sec.Pin = UIKit.Text(pin, "Label", s, "PIN", s.labelSize - 1f, s.textFaint, TextAlignmentOptions.Center, heading: true);
            UIKit.Stretch(sec.Pin.rectTransform);
            pin.gameObject.AddComponent<ClickArea>().Clicked += () => TogglePin(key);

            var click = row.gameObject.AddComponent<ClickArea>();
            click.Clicked += () => Toggle(key);
            var held = sec;
            click.Hovered += on => { held.Hover = on; if (owner != null) Paint(owner, key, held); };
            sections[key] = sec;
            return sec;
        }

        private static long Key(SlotRef slot) => ((long)slot.Kind << 32) | (uint)slot.Index;
    }
}
