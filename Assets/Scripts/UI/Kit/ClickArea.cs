using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Combat.UI
{
    // Plain left-click and hover callbacks for a rect with a raycast target.
    public class ClickArea : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action Clicked;
        public event Action<bool> Hovered;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) Clicked?.Invoke();
        }

        public void OnPointerEnter(PointerEventData e) => Hovered?.Invoke(true);
        public void OnPointerExit(PointerEventData e) => Hovered?.Invoke(false);
    }
}
