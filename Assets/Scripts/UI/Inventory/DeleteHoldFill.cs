using UnityEngine;
using UnityEngine.UI;

namespace Combat.UI
{
    // Red fill rising up an item tile while delete is held on it.
    public class DeleteHoldFill : MonoBehaviour
    {
        private InventoryScreen screen;
        private object item;
        private Image fill;

        public void Setup(InventoryScreen screen, object item, Image fill)
        {
            this.screen = screen;
            this.item = item;
            this.fill = fill;
        }

        private void Update()
        {
            if (screen == null || fill == null) return;
            float p = screen.DeleteProgress(item);
            fill.enabled = p > 0f;
            if (p > 0f) fill.rectTransform.anchorMax = new Vector2(1f, p);
        }
    }
}
