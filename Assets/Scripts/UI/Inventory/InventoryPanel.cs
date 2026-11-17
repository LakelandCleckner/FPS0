using UnityEngine;
using Combat.Perks;

namespace Combat.UI
{
    // A region of the inventory screen. Lay out its RectTransform by hand; it fills itself under _Generated.
    [RequireComponent(typeof(RectTransform))]
    public abstract class InventoryPanel : MonoBehaviour
    {
        public const string GeneratedName = "_Generated";

        private RectTransform current;

        // Updates in place when the panel supports it (play mode only), else builds from scratch.
        public void Rebuild(InventoryScreen screen)
        {
            if (screen == null || screen.Style == null) { Clear(); return; }
            if (current != null && Application.isPlaying && !screen.FullRebuild && CanRefresh)
            {
                Refresh(screen, current);
                return;
            }
            BeforeClear();
            Clear();
            current = UIKit.Stretch(UIKit.Node(transform, GeneratedName));
            Build(screen, current);
        }

        public void Clear()
        {
            current = null;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name != GeneratedName) continue;
                c.name = "_Removed";
                c.gameObject.SetActive(false);
                UIKit.Destroy(c.gameObject);
            }
        }

        protected abstract void Build(InventoryScreen screen, RectTransform root);

        // True when the content built last time can be updated by Refresh.
        protected virtual bool CanRefresh => false;

        protected virtual void Refresh(InventoryScreen screen, RectTransform root) { }

        // Last look at the old content before a full rebuild throws it away.
        protected virtual void BeforeClear() { }

        // Shaded panel surface with an outline, filling the root.
        protected static RectTransform Surface(RectTransform root, UIStyleSO style)
        {
            var bg = UIKit.Box(root, "Surface", style, style.panel, border: true, borderColor: style.panelBorder);
            UIKit.Shade(bg, style);
            UIKit.Stretch(bg.rectTransform);
            return bg.rectTransform;
        }

        // Uppercase heading, a rule running out to the right, and an optional note at the end.
        protected static RectTransform Heading(Transform parent, UIStyleSO style, string title, string note = null, Color? noteColor = null)
        {
            var row = UIKit.Node(parent, "Heading");
            UIKit.HStack(row, 12f);
            UIKit.Size(row, height: style.headingSize + 10f);

            UIKit.Text(row, "Title", style, title, style.headingSize, style.text, heading: true);

            var rule = UIKit.Solid(row, "Rule", style.divider);
            UIKit.Size(rule, height: 1f, flexWidth: 1f);

            if (!string.IsNullOrEmpty(note))
            {
                var n = UIKit.Text(row, "Note", style, note, style.labelSize + 1f, noteColor ?? style.textMuted,
                                   TMPro.TextAlignmentOptions.MidlineRight, heading: true);
                UIKit.Size(n, flexWidth: 0f);
            }
            return row;
        }

        // Tile with shaded fill, rarity wash, outline, selection mark and hover handling wired to the screen.
        // Wide = a card: the wash runs from the left instead of the top.
        protected static UITile Tile(Transform parent, string name, InventoryScreen screen, object item, bool interactable = true, bool wide = false)
        {
            var style = screen.Style;
            var fill = UIKit.Box(parent, name, style, style.tile);
            UIKit.Shade(fill, style);
            UIKit.RarityWash(fill.rectTransform, style, ItemText.RarityOf(item), wide);
            var borderColor = ItemText.BorderColor(style, item);
            var border = UIKit.Outline(fill.rectTransform, style, borderColor);

            float inset = style.EdgeInset;
            var mark = UIKit.Solid(fill.rectTransform, "SelectedMark", style.accent);
            UIKit.Anchor(mark.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(inset, 0f), new Vector2(-inset, 2f));
            UIKit.Ignore(mark);

            var tile = fill.gameObject.AddComponent<UITile>();
            tile.Item = item;
            tile.interactable = interactable;
            tile.Setup(fill, border, mark, style.tile, style.tileHover, borderColor, style.accent);
            tile.HoverChanged += screen.OnTileHover;
            return tile;
        }

        // Grid loot card for an owned item: rarity tag, monogram, name in its rarity colour, and the delete-hold fill.
        protected static UITile ItemTile(Transform grid, InventoryScreen screen, object item, float size = -1f)
        {
            var s = screen.Style;
            if (size <= 0f) size = s.gridTileSize;
            bool small = size < 100f;
            var tile = Tile(grid, "Item", screen, item);

            var rarity = ItemText.RarityOf(item);
            if (rarity.HasValue)
            {
                var tag = UIKit.Text(tile.transform, "Rarity", s, rarity.Value.ToString(), s.labelSize - 2f,
                                     rarity.Value == Rarity.Common ? s.textFaint : s.RarityColor(rarity.Value), TMPro.TextAlignmentOptions.TopLeft, heading: true);
                tag.characterSpacing = 1f;
                float inset = small ? 6f : 10f;
                UIKit.Anchor(tag.rectTransform, new Vector2(0f, 1f), new Vector2(0.85f, 1f), new Vector2(inset, -inset - 16f), new Vector2(0f, -inset));
            }

            var mono = UIKit.Text(tile.transform, "Monogram", s, ItemText.Monogram(item), size * 0.28f, s.text,
                                  TMPro.TextAlignmentOptions.Center, role: TextRole.Title);
            UIKit.Anchor(mono.rectTransform, new Vector2(0f, 0.34f), new Vector2(1f, 0.86f), Vector2.zero, Vector2.zero);

            var name = UIKit.Text(tile.transform, "Name", s, ItemText.Name(item), small ? s.labelSize + 1f : s.labelSize + 2f, ItemText.NameColor(s, item, s.textMuted),
                                  TMPro.TextAlignmentOptions.Center, lines: 2, role: TextRole.Title);
            float pad = small ? 4f : 8f;
            UIKit.Anchor(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.34f), new Vector2(pad, pad * 0.75f), new Vector2(-pad, 0f));

            var fill = UIKit.Box(tile.transform, "DeleteFill", s, new Color(s.negative.r, s.negative.g, s.negative.b, 0.35f));
            fill.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            UIKit.Anchor(fill.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            fill.enabled = false;
            fill.gameObject.AddComponent<DeleteHoldFill>().Setup(screen, item, fill);
            tile.gameObject.AddComponent<DragItem>().Setup(screen, item, null);
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
