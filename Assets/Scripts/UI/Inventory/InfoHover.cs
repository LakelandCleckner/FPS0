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
        private string title;
        private Color color;
        private IDescribable detail;
        private DescribeContext context;
        private bool over;

        public static void Attach(Graphic target, InventoryScreen screen, string title, Color color, IDescribable detail, DescribeContext ctx)
        {
            if (target == null || screen == null || detail == null) return;
            target.raycastTarget = true;
            var h = target.gameObject.AddComponent<InfoHover>();
            h.screen = screen;
            h.title = title;
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
            if (tip != null) tip.HoverInfo(screen, detail, title, color, null, detail, context, on);
        }
    }
}
