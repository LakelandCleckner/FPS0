using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Combat.UI
{
    // Vault tab grid: every carried item or every vault item, filtered and sorted. Click moves it to the other side.
    public class ItemGridPanel : InventoryPanel
    {
        public enum Mode { Carried, Vault }

        [SerializeField] private Mode mode = Mode.Vault;

        private readonly List<object> items = new List<object>();

        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            var view = screen.View;
            Surface(root, s);

            bool vault = mode == Mode.Vault;
            int total = vault ? view.VaultWeapons.Count + view.VaultArmor.Count : view.CarriedWeapons.Count + view.CarriedArmor.Count;
            if (vault) screen.Filter.Apply(view.VaultWeapons, view.VaultArmor, view.ArmorSlots, items);
            else screen.Filter.Apply(view.CarriedWeapons, view.CarriedArmor, view.ArmorSlots, items);

            var body = UIKit.Stretch(UIKit.Node(root, "Body"));
            UIKit.VStack(body, s.gap, UIKit.Pad(s.padding));

            string note = items.Count == total ? total.ToString() : items.Count + " / " + total;
            Heading(body, s, vault ? "Vault" : "Carried", note);
            UIKit.Text(body, "Hint", s, vault ? "Click to move to carried" : Caps(view), s.smallSize, s.textMuted, wrap: true);

            var area = UIKit.Node(body, "Area");
            UIKit.Size(area, flexHeight: 1f);
            var content = UIKit.Scroll(area, 0f);

            if (items.Count == 0)
            {
                string empty = total > 0 ? "Nothing matches the filters."
                    : vault ? "The vault is empty. Click a carried item to store it." : "Nothing carried.";
                UIKit.Text(content, "Empty", s, empty, s.smallSize, s.textFaint, wrap: true);
                return;
            }

            float width = root.rect.width - 2f * s.padding;
            var grid = Grid(content, s, items.Count, width);
            foreach (var item in items)
            {
                var tile = ItemTile(grid, screen, item);
                if (vault) tile.LeftClick += t => screen.MoveToCarried(t.Item);
                else tile.LeftClick += t => screen.MoveToVault(t.Item);
            }
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
