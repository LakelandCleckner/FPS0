using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace Combat.UI
{
    // Vault tab grid: every carried item or every vault item, filtered and sorted.
    // Click selects; double-click or Store moves it to the other side. Tiles slide when the list changes.
    public class ItemGridPanel : InventoryPanel
    {
        public enum Mode { Carried, Vault }

        [SerializeField] private Mode mode = Mode.Vault;

        private const string EmptyKey = "_empty";
        private readonly List<object> items = new List<object>();
        private SlideLayout layout;
        private TMP_Text note, hint, empty;

        protected override bool CanRefresh => layout != null;

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            Surface(root, s);

            var body = UIKit.Stretch(UIKit.Node(root, "Body"));
            UIKit.VStack(body, s.gap, UIKit.Pad(s.padding));
            var head = Heading(body, s, mode == Mode.Vault ? "Vault" : "Carried", " ");
            var n = head.Find("Note");
            note = n != null ? n.GetComponent<TMP_Text>() : null;
            hint = UIKit.Text(body, "Hint", s, "", s.smallSize, s.textMuted, wrap: true);

            var area = UIKit.Node(body, "Area");
            UIKit.Size(area, flexHeight: 1f);
            layout = UIKit.SlideScroll(area);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            empty = null;
            Refresh(screen, root);
        }

        protected override void Refresh(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            var view = screen.View;
            bool vault = mode == Mode.Vault;
            int total = vault ? view.VaultWeapons.Count + view.VaultArmor.Count : view.CarriedWeapons.Count + view.CarriedArmor.Count;
            if (vault) screen.Filter.Apply(view.VaultWeapons, view.VaultArmor, view.ArmorSlots, items);
            else screen.Filter.Apply(view.CarriedWeapons, view.CarriedArmor, view.ArmorSlots, items);

            if (note != null) note.text = items.Count == total ? total.ToString() : items.Count + " / " + total;
            hint.text = vault ? "Click to select. Double-click or press Store to take it out." : Caps(view);

            // The scroll area's width, or the panel's when the area isn't laid out yet.
            float width = layout.Width >= s.gridTileSize ? layout.Width : root.rect.width - 2f * s.padding;
            int columns = width > 0f ? Mathf.Max(1, Mathf.FloorToInt((width + s.gap) / (s.gridTileSize + s.gap))) : Mathf.Max(1, s.gridColumns);

            layout.Begin();
            float y = 0f;
            if (items.Count == 0)
            {
                if (empty == null) empty = UIKit.Text(layout.transform, "Empty", s, "", s.smallSize, s.textFaint, wrap: true);
                empty.text = total > 0 ? "Nothing matches the filters."
                    : vault ? "The vault is empty. Select a carried item and press Store to put it here." : "Nothing carried.";
                layout.Place(EmptyKey, empty.rectTransform, Vector2.zero, new Vector2(width, s.bodySize * 3f));
                y = s.bodySize * 3f;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var rect = layout.Get(item);
                UITile tile;
                if (rect == null)
                {
                    tile = ItemTile(layout.transform, screen, item);
                    tile.LeftClick += t => screen.Pick(t.Item);
                    tile.DoubleClick += t => screen.MoveAcross(t.Item);
                    rect = (RectTransform)tile.transform;
                }
                else tile = rect.GetComponent<UITile>();
                if (tile != null) tile.SetSelected(item == screen.Picked);

                int col = i % columns, row = i / columns;
                layout.Place(item, rect, new Vector2(col * (s.gridTileSize + s.gap), -row * (s.gridTileSize + s.gap)),
                             new Vector2(s.gridTileSize, s.gridTileSize));
            }
            if (items.Count > 0)
            {
                int rows = Mathf.CeilToInt(items.Count / (float)columns);
                y = rows * (s.gridTileSize + s.gap);
            }
            layout.End(y, screen.LastDeleted, new Color(s.negative.r, s.negative.g, s.negative.b, 0.55f));
            if (items.Count > 0) empty = null;
        }

        // Carried count against the cap for each slot: "S1 3/9  S2 0/9  Head 2/9 ...".
        private static string Caps(LoadoutView view)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < view.Weapons.Length; i++)
                sb.Append("S").Append(i + 1).Append(' ').Append(view.CarriedCount(new SlotRef(SlotKind.Weapon, i))).Append('/').Append(view.WeaponCapPerSlot).Append("   ");
            for (int i = 0; i < view.ArmorSlots.Count; i++)
                sb.Append(ItemText.SlotName(view.ArmorSlots[i])).Append(' ').Append(view.CarriedCount(new SlotRef(SlotKind.Armor, i))).Append('/').Append(view.ArmorCapPerSlot).Append("   ");
            return sb.ToString().TrimEnd();
        }
    }
}
