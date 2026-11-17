using UnityEngine;
using UnityEngine.EventSystems;

namespace Combat.UI
{
    // Lets an item tile or slot card be dragged; the screen draws the ghost and resolves the drop.
    public class DragItem : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private InventoryScreen screen;
        private object item;
        private SlotRef? from;
        private bool dragging;

        // From = the equipped slot when this is a slot card, else null.
        public void Setup(InventoryScreen owner, object dragged, SlotRef? fromSlot)
        {
            screen = owner;
            item = dragged;
            from = fromSlot;
        }

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = true;

        public void OnBeginDrag(PointerEventData e)
        {
            if (screen == null || item == null || e.button != PointerEventData.InputButton.Left) return;
            dragging = screen.BeginDrag(item, from, (RectTransform)transform, e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (dragging) screen.MoveDrag(e.position);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!dragging) return;
            dragging = false;
            screen.EndDrag();
        }

        private void OnDisable()
        {
            if (!dragging || screen == null) return;
            dragging = false;
            screen.EndDrag();
        }
    }
}
