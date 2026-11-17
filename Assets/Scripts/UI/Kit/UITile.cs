using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Combat.UI
{
    // Clickable tile: hover and selected visuals, left/right/double click, submit for gamepad later.
    public class UITile : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public event Action<UITile> LeftClick;
        public event Action<UITile> RightClick;
        public event Action<UITile> DoubleClick;
        public event Action<UITile, bool> HoverChanged;

        public object Item { get; set; }
        public int Index { get; set; }

        private Image fill, border, mark;
        private Color normalFill, hoverFill, normalBorder, accent;
        private bool selected, hovered;

        public void Setup(Image fill, Image border, Image mark, Color normalFill, Color hoverFill, Color normalBorder, Color accent)
        {
            transition = Transition.None;
            targetGraphic = fill;
            this.fill = fill;
            this.border = border;
            this.mark = mark;
            this.normalFill = normalFill;
            this.hoverFill = hoverFill;
            this.normalBorder = normalBorder;
            this.accent = accent;
            if (fill != null) fill.raycastTarget = true;
            Apply();
        }

        public void SetSelected(bool value)
        {
            selected = value;
            Apply();
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            // Keyboard or gamepad focus counts as hover, so the detail panel and item keys follow it.
            bool now = state == SelectionState.Highlighted || state == SelectionState.Pressed || state == SelectionState.Selected;
            if (now == hovered) return;
            hovered = now;
            Apply();
            HoverChanged?.Invoke(this, hovered);
        }

        // A tile removed while hovered still reports the hover ending.
        protected override void OnDisable()
        {
            base.OnDisable();
            if (!hovered) return;
            hovered = false;
            HoverChanged?.Invoke(this, false);
        }

        public override void OnSelect(BaseEventData e)
        {
            base.OnSelect(e);
            if (!(e is PointerEventData)) UIKit.ScrollIntoView((RectTransform)transform);
        }

        public void OnPointerClick(PointerEventData e)
        {
            // A mouse click must not leave the tile focused, or it would stay "hovered" after the pointer leaves.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            if (!IsInteractable()) return;
            if (e.button == PointerEventData.InputButton.Left)
            {
                LeftClick?.Invoke(this);
                if (e.clickCount == 2) DoubleClick?.Invoke(this);
            }
            else if (e.button == PointerEventData.InputButton.Right) RightClick?.Invoke(this);
        }

        public void OnSubmit(BaseEventData e)
        {
            if (IsInteractable()) LeftClick?.Invoke(this);
        }

        private void Apply()
        {
            if (fill != null) fill.color = hovered ? hoverFill : normalFill;
            if (border != null) border.color = selected ? accent : hovered ? Color.Lerp(normalBorder, accent, 0.45f) : normalBorder;
            if (mark != null) mark.enabled = selected;
        }
    }
}
