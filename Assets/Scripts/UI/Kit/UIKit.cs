using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Combat.UI
{
    // Code-built UI helpers. Everything made here is marked DontSave while Transient is set (edit-mode previews).
    public static class UIKit
    {
        public static bool Transient;

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (Transient) go.hideFlags = HideFlags.DontSave;
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        // Rounded fill, optionally with an outline child.
        public static Image Box(Transform parent, string name, UIStyleSO style, Color color, bool border = false, Color? borderColor = null)
        {
            var rt = Node(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = style.Fill;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            if (border) Outline(rt, style, borderColor ?? style.tileBorder);
            return img;
        }

        public static Image Outline(RectTransform parent, UIStyleSO style, Color color)
        {
            var rt = Stretch(Node(parent, "Border"));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = style.Border;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return img;
        }

        // Square-cornered solid block (rules, bar segments, accent marks).
        public static Image Solid(Transform parent, string name, Color color)
        {
            var rt = Node(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TMP_Text Text(Transform parent, string name, UIStyleSO style, string value, float size, Color color,
                                    TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool heading = false, bool wrap = false,
                                    int lines = 1)
        {
            var rt = Node(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = heading ? style.HeadingFont : style.BodyFont;
            t.text = value ?? "";
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            bool fit = !wrap;
            t.textWrappingMode = wrap || lines > 1 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            // Truncate, not Ellipsis: TMP re-looks up the ellipsis glyph per style and warns on bold text.
            t.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
            // Fitted text wraps up to its line count, then shrinks, then truncates.
            if (fit)
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = size;
                t.fontSizeMin = Mathf.Max(1f, size * style.minTextScale);
                if (lines > 1) Size(t, height: LineHeight(size) * lines);
            }
            if (heading)
            {
                t.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
                t.characterSpacing = style.headingSpacing;
            }
            return t;
        }

        // Height of one line of text at this size, for reserving room in layouts.
        public static float LineHeight(float size) => Mathf.Ceil(size * 1.2f);

        // Small uppercase label: section headings, column names, captions.
        public static TMP_Text Label(Transform parent, string name, UIStyleSO style, string value, Color? color = null)
            => Text(parent, name, style, value, style.labelSize, color ?? style.textMuted, TextAlignmentOptions.MidlineLeft, heading: true);

        public static LayoutElement Size(Component c, float width = -1f, float height = -1f, float flexWidth = -1f, float flexHeight = -1f)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) { le.minWidth = width; le.preferredWidth = width; }
            if (height >= 0f) { le.minHeight = height; le.preferredHeight = height; }
            if (flexWidth >= 0f) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0f) le.flexibleHeight = flexHeight;
            return le;
        }

        public static VerticalLayoutGroup VStack(Component c, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = padding ?? new RectOffset();
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HStack(Component c, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = padding ?? new RectOffset();
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        public static RectTransform Spacer(Transform parent, float flexWidth = 1f, float flexHeight = 0f)
        {
            var rt = Node(parent, "Spacer");
            Size(rt, flexWidth: flexWidth, flexHeight: flexHeight);
            return rt;
        }

        public static RectOffset Pad(float all) => Pad(all, all, all, all);

        public static RectOffset Pad(float left, float right, float top, float bottom)
            => new RectOffset(Mathf.RoundToInt(left), Mathf.RoundToInt(right), Mathf.RoundToInt(top), Mathf.RoundToInt(bottom));

        // Vertical scroll area; returns the content node to fill.
        public static RectTransform Scroll(RectTransform parent, float spacing, RectOffset padding = null)
        {
            var viewport = Stretch(Node(parent, "Viewport"));
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            var content = Node(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VStack(content, spacing, padding);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = parent.gameObject.GetComponent<ScrollRect>();
            if (scroll == null) scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content;
        }

        public static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}