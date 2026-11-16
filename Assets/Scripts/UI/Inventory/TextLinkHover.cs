using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Combat.Info;

namespace Combat.UI
{
    // Keyword links in a description: hover shows the keyword popup, click pins it.
    [RequireComponent(typeof(TMP_Text))]
    public class TextLinkHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
    {
        private InventoryScreen screen;
        private List<KeywordLink> links;
        private DescribeContext context;
        private TMP_Text text;
        private int hovered = -1;

        public void Setup(InventoryScreen owner, List<KeywordLink> keywordLinks, DescribeContext ctx)
        {
            screen = owner;
            links = keywordLinks;
            context = ctx;
            text = GetComponent<TMP_Text>();
            text.raycastTarget = true;
        }

        private void OnDisable() => SetHovered(-1);

        public void OnPointerEnter(PointerEventData e) => Check(e);
        public void OnPointerMove(PointerEventData e) => Check(e);
        public void OnPointerExit(PointerEventData e) => SetHovered(-1);

        public void OnPointerClick(PointerEventData e)
        {
            Check(e);
            if (hovered >= 0 && screen != null && screen.Tooltip != null) screen.Tooltip.ToggleKeywordPin(screen, links[hovered], context);
        }

        private void Check(PointerEventData e)
        {
            if (text == null || links == null) return;
            int i = TMP_TextUtilities.FindIntersectingLink(text, e.position, e.enterEventCamera);
            int index = -1;
            if (i >= 0 && int.TryParse(text.textInfo.linkInfo[i].GetLinkID(), out int id) && id >= 0 && id < links.Count) index = id;
            SetHovered(index);
        }

        private void SetHovered(int index)
        {
            if (index == hovered) return;
            var tip = screen != null ? screen.Tooltip : null;
            if (tip != null && hovered >= 0) tip.HoverKeyword(screen, links[hovered], context, false);
            hovered = index;
            if (tip != null && hovered >= 0) tip.HoverKeyword(screen, links[hovered], context, true);
        }
    }
}
