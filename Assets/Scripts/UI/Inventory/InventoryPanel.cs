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
            var borderColor = ItemText.BorderColor(style, item);
            var border = UIKit.Outline(fill.rectTransform, style, borderColor);

            var mark = UIKit.Solid(fill.rectTransform, "SelectedMark", style.accent);
            UIKit.Anchor(mark.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 6f), new Vector2(3f, -6f));
            mark.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;

            var tile = fill.gameObject.AddComponent<UITile>();
            tile.Item = item;
            tile.interactable = interactable;
            tile.Setup(fill, border, mark, style.tile, style.tileHover, borderColor, style.accent);
            tile.HoverChanged += screen.OnTileHover;
            return tile;
        }

        // Grid tile for an owned item: monogram, name in its rarity colour, and the delete-hold fill.
        protected static UITile ItemTile(Transform grid, InventoryScreen screen, object item)
        {
            var s = screen.Style;
            var tile = Tile(grid, "Item", screen, item);

            var mono = UIKit.Text(tile.transform, "Monogram", s, ItemText.Monogram(item), s.gridTileSize * 0.26f, s.text,
                                  TMPro.TextAlignmentOptions.Center, heading: true);
            UIKit.Anchor(mono.rectTransform, new Vector2(0f, 0.3f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            var name = UIKit.Text(tile.transform, "Name", s, ItemText.Name(item), s.labelSize + 1f, ItemText.NameColor(s, item, s.textMuted),
                                  TMPro.TextAlignmentOptions.Center, lines: 2);
            UIKit.Anchor(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.3f), new Vector2(8f, 6f), new Vector2(-8f, 0f));

            var fill = UIKit.Box(tile.transform, "DeleteFill", s, new Color(s.negative.r, s.negative.g, s.negative.b, 0.35f));
            fill.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            UIKit.Anchor(fill.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            fill.enabled = false;
            fill.gameObject.AddComponent<DeleteHoldFill>().Setup(screen, item, fill);
            return tile;
        }

        // Fixed-size tile grid; columns fit the width when it's known.
        protected static RectTransform Grid(Transform parent, UIStyleSO s, int count, float width)
        {
            int columns = width > 0f ? Mathf.Max(1, Mathf.FloorToInt((width + s.gap) / (s.gridTileSize + s.gap))) : Mathf.Max(1, s.gridColumns);
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));

            var grid = UIKit.Node(parent, "Grid");
            var g = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            g.cellSize = new Vector2(s.gridTileSize, s.gridTileSize);
            g.spacing = new Vector2(s.gap, s.gap);
            g.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.childAlignment = TextAnchor.UpperLeft;
            UIKit.Size(grid, height: rows * s.gridTileSize + (rows - 1) * s.gap);
            return grid;
        }
    }
}
