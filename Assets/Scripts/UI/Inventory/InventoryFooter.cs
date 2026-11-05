using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Combat.UI
{
    // Messages on the left, control hints on the right. Key names come from the live bindings.
    public class InventoryFooter : InventoryPanel
    {
        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            UIKit.HStack(root, s.sectionGap, null, TextAnchor.MiddleLeft);

            bool hasMessage = !string.IsNullOrEmpty(screen.Message);
            var msg = UIKit.Text(root, "Message", s, hasMessage ? screen.Message : "", s.bodySize - 2f,
                                 s.negative, TextAlignmentOptions.MidlineLeft);
            UIKit.Size(msg, flexWidth: 1f);

            var input = screen.Input;
            string close = input != null ? input.Menu.Inventory.GetBindingDisplayString() : "Tab";
            string back = input != null ? input.UI.Cancel.GetBindingDisplayString() : "Esc";

            string store = Key(input != null ? input.UI.Store : null, "V");
            string delete = "Hold " + Key(input != null ? input.UI.Delete : null, "X");
            if (screen.Tab == InventoryTab.Loadout)
            {
                Hint(root, s, "LMB", "Select / Equip");
                Hint(root, s, "RMB", "Unequip");
                Hint(root, s, store, "Store");
                Hint(root, s, delete, "Delete");
            }
            else if (screen.Tab == InventoryTab.Vault)
            {
                Hint(root, s, "LMB", "Move");
                Hint(root, s, store, "Move");
                Hint(root, s, delete, "Delete");
            }
            Hint(root, s, string.IsNullOrEmpty(close) ? "Tab" : close, "Close");
            if (!string.IsNullOrEmpty(back) && back != close) Hint(root, s, back, "Back");
        }

        // First binding's display name (the keyboard one), or the fallback.
        private static string Key(InputAction action, string fallback)
        {
            string k = action != null && action.bindings.Count > 0 ? action.GetBindingDisplayString(0) : "";
            return string.IsNullOrEmpty(k) ? fallback : k;
        }

        private static void Hint(Transform parent, UIStyleSO s, string key, string action)
        {
            var row = UIKit.Node(parent, "Hint_" + action);
            UIKit.HStack(row, 8f);

            var cap = UIKit.Box(row, "Key", s, s.tileHover, true);
            UIKit.HStack(cap, 0f, UIKit.Pad(8f, 8f, 3f, 3f), TextAnchor.MiddleCenter);
            UIKit.Size(cap, height: s.chipSize);
            var k = UIKit.Text(cap.transform, "Text", s, key, s.labelSize, s.text, TextAlignmentOptions.Center, heading: true);
            k.characterSpacing = 0f;

            UIKit.Text(row, "Action", s, action, s.smallSize, s.textMuted);
        }
    }
}
