using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Combat.UI
{
    // Clickable tile: hover and selected visuals, left/right click, submit for gamepad later.
    public class UITile : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public event Action<UITile> LeftClick;
        public event Action<UITile> RightClick;
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
            bool now = state == SelectionState.Highlighted || state == SelectionState.Pressed;
            if (now == hovered) return;
            hovered = now;
            Apply();
            HoverChanged?.Invoke(this, hovered);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!IsInteractable()) return;
            if (e.button == PointerEventData.InputButton.Left) LeftClick?.Invoke(this);
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
