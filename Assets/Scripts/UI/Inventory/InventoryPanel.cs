using UnityEngine;

namespace Combat.UI
{
    // A region of the inventory screen. Lay out its RectTransform by hand; it fills itself under _Generated.
    [RequireComponent(typeof(RectTransform))]
    public abstract class InventoryPanel : MonoBehaviour
    {
        public const string GeneratedName = "_Generated";

        public void Rebuild(InventoryScreen screen)
        {
            Clear();
            if (screen == null || screen.Style == null) return;
            var root = UIKit.Stretch(UIKit.Node(transform, GeneratedName));
            Build(screen, root);
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name == GeneratedName) UIKit.Destroy(c.gameObject);
            }
        }

        protected abstract void Build(InventoryScreen screen, RectTransform root);

        // Panel surface with an outline, filling the root.
        protected static RectTransform Surface(RectTransform root, UIStyleSO style)
        {
            var bg = UIKit.Box(root, "Surface", style, style.panel, border: true, borderColor: style.panelBorder);
            UIKit.Stretch(bg.rectTransform);
            return bg.rectTransform;
        }

        // Uppercase heading with an accent tick and an optional right-side note.
        protected static RectTransform Heading(Transform parent, UIStyleSO style, string title, string note = null, Color? noteColor = null)
        {
            var row = UIKit.Node(parent, "Heading");
            UIKit.HStack(row, 10f);
            UIKit.Size(row, height: style.headingSize + 10f);

            var tick = UIKit.Solid(row, "Tick", style.accent);
            UIKit.Size(tick, width: 3f, height: style.headingSize);

            var t = UIKit.Text(row, "Title", style, title, style.headingSize, style.text, heading: true);
            UIKit.Size(t, flexWidth: 1f);

            if (!string.IsNullOrEmpty(note))
            {
                var n = UIKit.Text(row, "Note", style, note, style.labelSize + 1f, noteColor ?? style.textMuted,
                                   TMPro.TextAlignmentOptions.MidlineRight, heading: true);
                UIKit.Size(n, flexWidth: 0f);
            }
            return row;
        }

        // Tile with fill, outline, selection mark and hover handling wired to the screen.
        protected static UITile Tile(Transform parent, string name, InventoryScreen screen, object item, bool interactable = true)
        {
            var style = screen.Style;
            var fill = UIKit.Box(parent, name, style, style.tile);
            var border = UIKit.Outline(fill.rectTransform, style, style.tileBorder);

            var mark = UIKit.Solid(fill.rectTransform, "SelectedMark", style.accent);
            UIKit.Anchor(mark.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 6f), new Vector2(3f, -6f));
            mark.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;

            var tile = fill.gameObject.AddComponent<UITile>();
            tile.Item = item;
            tile.interactable = interactable;
            tile.Setup(fill, border, mark, style.tile, style.tileHover, style.tileBorder, style.accent);
            tile.HoverChanged += screen.OnTileHover;
            return tile;
        }
    }
}
