using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Combat.UI
{
    // Look of the menu screens: type, colours, shape and spacing. Panels rebuild from this.
    [CreateAssetMenu(fileName = "UIStyle", menuName = "Combat/UI/UI Style")]
    public class UIStyleSO : ScriptableObject
    {
        [Header("Type")]
        [Tooltip("Body and numbers. Empty = TextMeshPro's default font.")]
        public TMP_FontAsset font;
        [Tooltip("Titles, headings and labels. Empty = Font.")]
        public TMP_FontAsset headingFont;
        public float titleSize = 34f;
        public float headingSize = 14f;
        public float bodySize = 19f;
        public float smallSize = 15f;
        public float labelSize = 12f;
        [Tooltip("Letter spacing on uppercase headings and labels.")]
        public float headingSpacing = 8f;

        [Header("Surfaces")]
        public Color backdrop = new Color(0.02f, 0.03f, 0.04f, 0.86f);
        public Color panel = new Color(0.07f, 0.08f, 0.095f, 0.94f);
        public Color panelBorder = new Color(1f, 1f, 1f, 0.06f);
        public Color tile = new Color(0.115f, 0.13f, 0.15f, 1f);
        public Color tileHover = new Color(0.16f, 0.185f, 0.21f, 1f);
        public Color tileEmpty = new Color(1f, 1f, 1f, 0.025f);
        public Color tileBorder = new Color(1f, 1f, 1f, 0.07f);
        public Color divider = new Color(1f, 1f, 1f, 0.08f);

        [Header("Text")]
        public Color text = new Color(0.93f, 0.94f, 0.95f, 1f);
        public Color textMuted = new Color(0.6f, 0.65f, 0.69f, 1f);
        public Color textFaint = new Color(0.4f, 0.44f, 0.48f, 1f);

        [Header("Accent and state")]
        public Color accent = new Color(0.96f, 0.72f, 0.3f, 1f);
        public Color positive = new Color(0.5f, 0.86f, 0.58f, 1f);
        public Color negative = new Color(0.96f, 0.44f, 0.4f, 1f);

        [Header("Stat bars")]
        public Color barTrack = new Color(1f, 1f, 1f, 0.07f);
        public Color barBase = new Color(0.86f, 0.89f, 0.91f, 1f);
        public Color barPerk = new Color(0.47f, 0.76f, 1f, 1f);
        public Color barArmor = new Color(0.6f, 0.88f, 0.62f, 1f);
        public Color barLoss = new Color(0.96f, 0.44f, 0.4f, 0.75f);
        public float barHeight = 8f;

        [Header("Shape")]
        [Range(0, 24)] public int cornerRadius = 6;
        [Range(1, 4)] public int borderWidth = 1;

        [Header("Spacing")]
        public float padding = 22f;
        public float gap = 10f;
        public float sectionGap = 22f;

        [Header("Sizes")]
        public float weaponSlotHeight = 100f;
        public float armorSlotHeight = 58f;
        public float gridTileSize = 118f;
        [Min(1)] public int gridColumns = 3;
        public float perkIconSize = 46f;
        public float chipSize = 26f;

        private readonly Dictionary<int, Sprite> fills = new Dictionary<int, Sprite>();
        private readonly Dictionary<long, Sprite> borders = new Dictionary<long, Sprite>();

        public TMP_FontAsset BodyFont => font != null ? font : TMP_Settings.defaultFontAsset;
        public TMP_FontAsset HeadingFont => headingFont != null ? headingFont : BodyFont;

        public Sprite Fill => FillSprite(cornerRadius);
        public Sprite Border => BorderSprite(cornerRadius, borderWidth);

        private void OnValidate()
        {
            fills.Clear();
            borders.Clear();
        }

        public Sprite FillSprite(int radius)
        {
            radius = Mathf.Max(0, radius);
            if (fills.TryGetValue(radius, out var s) && s != null) return s;
            s = MakeSprite(radius, -1);
            fills[radius] = s;
            return s;
        }

        public Sprite BorderSprite(int radius, int width)
        {
            radius = Mathf.Max(0, radius);
            long key = ((long)radius << 8) | (uint)width;
            if (borders.TryGetValue(key, out var s) && s != null) return s;
            s = MakeSprite(radius, width);
            borders[key] = s;
            return s;
        }

        // Rounded rect as a 9-slice sprite; width < 0 = filled, else an outline that many pixels thick.
        private static Sprite MakeSprite(int radius, int width)
        {
            int edge = Mathf.Max(radius, Mathf.Max(width, 1)) + 2;
            int size = edge * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f - half, y + 0.5f - half, half - 0.5f, radius);
                    float outer = Mathf.Clamp01(0.5f - d);
                    float a = width < 0 ? outer : outer - Mathf.Clamp01(0.5f - (d + width));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(edge, edge, edge, edge));
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static float RoundedRectDistance(float px, float py, float halfSize, float r)
        {
            float qx = Mathf.Abs(px) - (halfSize - r);
            float qy = Mathf.Abs(py) - (halfSize - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
    }
}
