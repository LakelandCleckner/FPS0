using TMPro;
using UnityEngine;

namespace Combat.UI
{
    // Title, tabs and loadout state along the top of the screen.
    public class InventoryHeader : InventoryPanel
    {
        protected override void Build(InventoryScreen screen, RectTransform root)
        {
            var s = screen.Style;
            UIKit.HStack(root, s.sectionGap, UIKit.Pad(0f, 0f, 0f, 0f), TextAnchor.MiddleLeft);

            var titleBlock = UIKit.Node(root, "TitleBlock");
            UIKit.VStack(titleBlock, 2f, null, TextAnchor.MiddleLeft);
            if (screen.View.IsPreview) UIKit.Label(titleBlock, "Eyebrow", s, "Edit-mode preview", s.accent);
            UIKit.Text(titleBlock, "Title", s, "Inventory", s.titleSize, s.text, heading: true);

            var rule = UIKit.Solid(root, "Rule", s.divider);
            UIKit.Size(rule, width: 1f, height: s.titleSize + 18f);

            Tab(root, screen, "Loadout", InventoryTab.Loadout);
            Tab(root, screen, "Character", InventoryTab.Character);
            Tab(root, screen, "Vault", InventoryTab.Vault);

            UIKit.Spacer(root);

            if (screen.View.Locked)
            {
                var badge = UIKit.Box(root, "Locked", s, new Color(s.negative.r, s.negative.g, s.negative.b, 0.14f), true, s.negative);
                UIKit.HStack(badge, 0f, UIKit.Pad(14f, 14f, 6f, 6f), TextAnchor.MiddleCenter);
                UIKit.Text(badge.transform, "Text", s, "Loadout locked", s.labelSize + 1f, s.negative, TextAlignmentOptions.Center, heading: true);
            }
        }

        private static void Tab(RectTransform row, InventoryScreen screen, string label, InventoryTab tab)
        {
            var s = screen.Style;
            bool active = screen.Tab == tab;

            var node = UIKit.Node(row, "Tab_" + label);
            UIKit.VStack(node, 8f, UIKit.Pad(4f, 4f, 10f, 0f), TextAnchor.MiddleCenter);
            var hit = node.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            var text = UIKit.Text(node, "Text", s, label, s.headingSize + 2f, active ? s.text : s.textMuted, TextAlignmentOptions.Center, heading: true);
            var bar = UIKit.Solid(node, "Underline", active ? s.accent : new Color(0f, 0f, 0f, 0f));
            UIKit.Size(bar, height: 2f);

            var tile = node.gameObject.AddComponent<UITile>();
            tile.Setup(hit, null, null, hit.color, new Color(1f, 1f, 1f, 0.03f), Color.clear, s.accent);
            tile.LeftClick += _ => screen.SetTab(tab);
            tile.HoverChanged += (_, on) => { if (!active && text != null) text.color = on ? s.text : s.textMuted; };
        }
    }
}
