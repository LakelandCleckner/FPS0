using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Combat.UI
{
    public enum DropKind { CarriedGrid, VaultGrid, LoadoutCarried, Slot }

    // Somewhere a dragged item can land. Lights up while a valid drag is over it.
    public class DropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private InventoryScreen screen;
        private DropKind kind;
        private SlotRef slot;
        private Image highlight;

        public static DropTarget Attach(RectTransform target, InventoryScreen owner, DropKind kind, SlotRef slot = default)
        {
            var s = owner.Style;
            var t = target.gameObject.AddComponent<DropTarget>();
            t.screen = owner;
            t.kind = kind;
            t.slot = slot;
            t.highlight = UIKit.Outline(target, s, s.accent);
            t.highlight.enabled = false;
            return t;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (screen != null && screen.CanDrop(kind, slot)) screen.HoverDrop(this);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (screen != null) screen.LeaveDrop(this);
        }

        public void OnDrop(PointerEventData e)
        {
            if (screen != null) screen.Drop(kind, slot);
        }

        public void SetLit(bool on)
        {
            if (highlight != null) highlight.enabled = on;
        }
    }
}
