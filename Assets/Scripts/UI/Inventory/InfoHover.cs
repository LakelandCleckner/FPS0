using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Combat.Info;

namespace Combat.UI
{
    // Shows a titled popup for something describable (a perk chip) while the pointer is over it. Clicks pass through to the parent.
    public class InfoHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private InventoryScreen screen;
        private object key;
        private string title, rule;
        private Color color;
        private IDescribable detail;
        private DescribeContext context;
        private bool over;

        public static void Attach(Graphic target, InventoryScreen screen, string title, Color color, IDescribable detail, DescribeContext ctx)
            => Attach(target, screen, detail, title, color, null, detail, ctx);

        // Key identifies the popup (same key = same popup); rule is an optional line above the generated detail.
        public static void Attach(Graphic target, InventoryScreen screen, object key, string title, Color color, string rule, IDescribable detail, DescribeContext ctx)
        {
            if (target == null || screen == null || key == null || (detail == null && string.IsNullOrWhiteSpace(rule))) return;
            target.raycastTarget = true;
            var h = target.gameObject.AddComponent<InfoHover>();
            h.screen = screen;
            h.key = key;
            h.title = title;
            h.rule = rule;
            h.color = color;
            h.detail = detail;
            h.context = ctx;
        }

        public void OnPointerEnter(PointerEventData e) => Set(true);
        public void OnPointerExit(PointerEventData e) => Set(false);
        private void OnDisable() => Set(false);

        private void Set(bool on)
        {
            if (over == on) return;
            over = on;
            var tip = screen != null ? screen.Tooltip : null;
            if (tip != null) tip.HoverInfo(screen, key, title, color, rule, detail, context, on);
        }
    }
}
