using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Combat.Enemies;
using Combat.Perks;

namespace Combat.UI
{
    public enum CornerStyle { Rounded, Cut }

    // Look of the menu screens: type, colours, shape and spacing. Panels rebuild from this.
    [CreateAssetMenu(fileName = "UIStyle", menuName = "Combat/UI/UI Style")]
    public class UIStyleSO : ScriptableObject
    {
        [Header("Type")]
        [Tooltip("Body text and descriptions. Empty = TextMeshPro's default font.")]
        public TMP_FontAsset font;
        [Tooltip("Headings and labels. Empty = Font.")]
        public TMP_FontAsset headingFont;
        [Tooltip("Screen titles and item names. Empty = Heading Font.")]
        public TMP_FontAsset titleFont;
        [Tooltip("Stat values, counts and timers. Empty = Font.")]
        public TMP_FontAsset numberFont;
        [Tooltip("Built-in keywords (slow, pool, explode...) for generated descriptions.")]
        public Combat.Info.KeywordLibrarySO keywords;
        public float titleSize = 34f;
        public float headingSize = 14f;
        public float bodySize = 19f;
        public float smallSize = 15f;
        public float labelSize = 12f;
        [Tooltip("Letter spacing on uppercase headings and labels.")]
        public float headingSpacing = 2f;
        [Tooltip("Single-line text shrinks to fit, down to this fraction of its size, then truncates.")]
        [Range(0.2f, 1f)] public float minTextScale = 0.4f;

        [Header("Surfaces")]
        public Color backdrop = new Color(0.015f, 0.014f, 0.016f, 0.9f);
        public Color panel = new Color(0.055f, 0.053f, 0.058f, 0.96f);
        public Color panelBorder = new Color(1f, 0.95f, 0.85f, 0.07f);
        public Color tile = new Color(0.095f, 0.091f, 0.098f, 1f);
        public Color tileHover = new Color(0.15f, 0.142f, 0.145f, 1f);
        public Color tileEmpty = new Color(1f, 0.95f, 0.85f, 0.025f);
        public Color tileBorder = new Color(1f, 0.95f, 0.85f, 0.08f);
        public Color divider = new Color(1f, 0.95f, 0.85f, 0.1f);
        [Tooltip("Tile and panel shading: bottom brightness relative to the top.")]
        [Range(0f, 1f)] public float tileShade = 0.72f;

        [Header("Text")]
        public Color text = new Color(0.94f, 0.92f, 0.88f, 1f);
        public Color textMuted = new Color(0.66f, 0.63f, 0.58f, 1f);
        public Color textFaint = new Color(0.45f, 0.43f, 0.4f, 1f);

        [Header("Accent and state")]
        public Color accent = new Color(0.86f, 0.7f, 0.42f, 1f);
        public Color positive = new Color(0.5f, 0.86f, 0.58f, 1f);
        public Color negative = new Color(0.96f, 0.44f, 0.4f, 1f);

        [Header("Rarity")]
        public Color rarityCommon = new Color(0.72f, 0.76f, 0.8f, 1f);
        public Color rarityUncommon = new Color(0.42f, 0.82f, 0.45f, 1f);
        public Color rarityRare = new Color(0.38f, 0.62f, 1f, 1f);
        public Color rarityLegendary = new Color(0.72f, 0.45f, 1f, 1f);
        public Color rarityMythic = new Color(1f, 0.85f, 0.35f, 1f);
        [Tooltip("Tile border strength for Uncommon to Legendary. Common keeps the normal border, Mythic is solid.")]
        [Range(0f, 1f)] public float rarityBorderAlpha = 0.55f;
        [Tooltip("Rarity colour strength at the top (or left) of an item, fading out. Scales up per tier; Common gets none.")]
        [Range(0f, 1f)] public float rarityWash = 0.42f;
        [Tooltip("Rarity band along the top edge of items.")]
        public float rarityBandHeight = 3f;

        [Header("Enemy ranks")]
        public Color rankMinor = new Color(0.86f, 0.25f, 0.22f, 1f);
        public Color rankMajor = new Color(1f, 0.66f, 0.2f, 1f);
        public Color rankElite = new Color(0.78f, 0.42f, 1f, 1f);
        public Color rankMiniboss = new Color(1f, 0.86f, 0.35f, 1f);
        public Color rankBoss = new Color(1f, 0.86f, 0.35f, 1f);
        [Tooltip("Optional rank icons, white fill tinted by the rank colour. Empty = pips (Minor 1 to Miniboss 4).")]
        public Sprite rankIconMinor;
        public Sprite rankIconMajor;
        public Sprite rankIconElite;
        public Sprite rankIconMiniboss;
        public Sprite rankIconBoss;
        [Tooltip("Default shield colour when a shield doesn't set its own.")]
        public Color enemyShield = new Color(0.45f, 0.8f, 1f, 1f);

        [Header("Damage types")]
        [Tooltip("Inline element icons for rich text (<sprite name=\"Fire\">). Built by FPS0/Art/Build Damage Type Icon Sprites.")]
        public TMP_SpriteAsset inlineIcons;

        [Header("Stat bars")]
        public Color barTrack = new Color(1f, 1f, 1f, 0.07f);
        public Color barBase = new Color(0.86f, 0.89f, 0.91f, 1f);
        public Color barPerk = new Color(0.47f, 0.76f, 1f, 1f);
        public Color barArmor = new Color(0.6f, 0.88f, 0.62f, 1f);
        public Color barAttribute = new Color(0.98f, 0.74f, 0.36f, 1f);
        public Color barLoss = new Color(0.96f, 0.44f, 0.4f, 0.75f);
        public float barHeight = 8f;

        [Header("Shape")]
        public CornerStyle corners = CornerStyle.Cut;
        [Range(0, 24)] public int cornerRadius = 6;
        [Tooltip("Corner cut on panels, tiles and cards.")]
        [Range(0, 32)] public int cutSize = 12;
        [Tooltip("Corner cut on chips, key caps and tags.")]
        [Range(0, 16)] public int cutSizeSmall = 5;
        [Range(1, 4)] public int borderWidth = 1;

        [Header("Spacing")]
        public float padding = 22f;
        public float gap = 10f;
        public float sectionGap = 22f;

        [Header("Sizes")]
        public float weaponSlotHeight = 78f;
        public float armorSlotHeight = 58f;
        public float gridTileSize = 118f;
        [Min(1)] public int gridColumns = 3;
        public float perkIconSize = 46f;
        public float chipSize = 26f;
        [Tooltip("Perk chips on the equipped slot cards.")]
        public float cardChipSize = 22f;
        [Tooltip("Tiles per row in the loadout tab's carried sections; tile size fits the panel width.")]
        [Min(1)] public int carriedColumns = 5;
        [Tooltip("Carried sections open for the selected slot and close for the rest; pinned sections always stay open.")]
        public bool carriedAutoOpen = true;
        [Tooltip("Perk chips per row on the equipped cards.")]
        [Min(1)] public int chipsPerRow = 4;

        private readonly Dictionary<long, Sprite> sprites = new Dictionary<long, Sprite>();

        // Wash strength per tier, from Rarity Wash.
        public float WashStrength(Rarity r)
        {
            switch (r)
            {
                case Rarity.Uncommon: return rarityWash * 0.6f;
                case Rarity.Rare: return rarityWash * 0.75f;
                case Rarity.Legendary: return rarityWash * 0.9f;
                case Rarity.Mythic: return rarityWash;
                default: return 0f;
            }
        }

        public Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Uncommon: return rarityUncommon;
                case Rarity.Rare: return rarityRare;
                case Rarity.Legendary: return rarityLegendary;
                case Rarity.Mythic: return rarityMythic;
                default: return rarityCommon;
            }
        }

        public Color RankColor(EnemyRank r)
        {
            switch (r)
            {
                case EnemyRank.Major: return rankMajor;
                case EnemyRank.Elite: return rankElite;
                case EnemyRank.Miniboss: return rankMiniboss;
                case EnemyRank.Boss: return rankBoss;
                default: return rankMinor;
            }
        }

        public Sprite RankIcon(EnemyRank r)
        {
            switch (r)
            {
                case EnemyRank.Major: return rankIconMajor;
                case EnemyRank.Elite: return rankIconElite;
                case EnemyRank.Miniboss: return rankIconMiniboss;
                case EnemyRank.Minor: return rankIconMinor;
                case EnemyRank.Boss: return rankIconBoss;
                default: return null;
            }
        }

        public TMP_FontAsset BodyFont => font != null ? font : TMP_Settings.defaultFontAsset;
        public TMP_FontAsset HeadingFont => headingFont != null ? headingFont : BodyFont;
        public TMP_FontAsset TitleFont => titleFont != null ? titleFont : HeadingFont;
        public TMP_FontAsset NumberFont => numberFont != null ? numberFont : BodyFont;

        // True when the role has a real font of its own, so it isn't faked bold.
        public bool HasHeadingFont => headingFont != null;
        public bool HasTitleFont => titleFont != null || headingFont != null;

        private bool Cut => corners == CornerStyle.Cut;
        public Sprite Fill => Cut ? CutFillSprite(cutSize) : FillSprite(cornerRadius);
        public Sprite Border => Cut ? CutBorderSprite(cutSize, borderWidth) : BorderSprite(cornerRadius, borderWidth);
        public Sprite SmallFill => Cut ? CutFillSprite(cutSizeSmall) : FillSprite(cornerRadius);
        public Sprite SmallBorder => Cut ? CutBorderSprite(cutSizeSmall, borderWidth) : BorderSprite(cornerRadius, borderWidth);

        // Distance from the box edge to where a straight top edge starts.
        public float EdgeInset => Cut ? cutSize : cornerRadius;

        private void OnValidate() => sprites.Clear();

        public Sprite FillSprite(int radius) => Get(false, radius, -1);
        public Sprite BorderSprite(int radius, int width) => Get(false, radius, width);
        public Sprite CutFillSprite(int cut) => Get(true, cut, -1);
        public Sprite CutBorderSprite(int cut, int width) => Get(true, cut, width);

        private Sprite Get(bool cut, int size, int width)
        {
            size = Mathf.Max(0, size);
            long key = (cut ? 1L << 40 : 0L) | ((long)size << 8) | (uint)(width < 0 ? 255 : width);
            if (sprites.TryGetValue(key, out var s) && s != null) return s;
            s = MakeSprite(size, width, cut);
            sprites[key] = s;
            return s;
        }

        // Rounded or corner-cut rect as a 9-slice sprite; width < 0 = filled, else an outline that many pixels thick.
        private static Sprite MakeSprite(int radius, int width, bool cut)
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
                    float lx = x + 0.5f - half, ly = y + 0.5f - half;
                    float d = cut ? CutRectDistance(lx, ly, half - 0.5f, radius) : RoundedRectDistance(lx, ly, half - 0.5f, radius);
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

        // Square with 45-degree cuts of size c at each corner.
        private static float CutRectDistance(float px, float py, float halfSize, float c)
        {
            float ax = Mathf.Abs(px), ay = Mathf.Abs(py);
            float box = Mathf.Max(ax, ay) - halfSize;
            float diag = (ax + ay - (2f * halfSize - c)) * 0.70710678f;
            return Mathf.Max(box, diag);
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
