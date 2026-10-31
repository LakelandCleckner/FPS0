using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Combat.Perks;

namespace Combat.UI
{
    // Row of tiles for perks that are doing something right now: buffs with stacks and timers,
    // counters building toward a trigger, conditional effects that are on. Perks on a stowed weapon are dimmed.
    // Tiles are built in code; put this on an empty RectTransform where the row should start.
    public class PerkTracker : MonoBehaviour
    {
        [Header("Tiles")]
        [SerializeField] private float tileSize = 44f;
        [SerializeField] private float spacing = 4f;
        [SerializeField] private float fontSize = 16f;
        [Tooltip("Optional. Empty = TextMeshPro's default font.")]
        [SerializeField] private TMP_FontAsset font;

        [Header("Colors")]
        [SerializeField] private Color background = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color timerColor = new Color(1f, 0.82f, 0.35f);
        [Range(0f, 1f)] [SerializeField] private float stowedAlpha = 0.4f;

        private sealed class Tile
        {
            public GameObject Root;
            public CanvasGroup Group;
            public TMP_Text Label;
            public Image Icon;
            public TMP_Text Corner;
            public RectTransform Timer;
            public PerkStatus Status;
        }

        private readonly Dictionary<PerkRuntime, Tile> tiles = new Dictionary<PerkRuntime, Tile>();
        private readonly List<PerkRuntime> scratch = new List<PerkRuntime>();

        // Before any perk attaches (perks attach in Start), so no attach is missed.
        private void Awake()
        {
            var layout = GetComponent<HorizontalLayoutGroup>();
            if (layout == null) layout = gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
        }

        private void OnEnable() => PerkRuntime.AnyStatusChanged += Refresh;

        private void OnDisable()
        {
            PerkRuntime.AnyStatusChanged -= Refresh;
            foreach (var t in tiles.Values) if (t.Root != null) Destroy(t.Root);
            tiles.Clear();
        }

        private void Refresh(PerkRuntime rt)
        {
            bool show = !rt.Detached && rt.TryGetStatus(out var status) && status.Active;

            if (!show)
            {
                if (tiles.TryGetValue(rt, out var gone))
                {
                    Destroy(gone.Root);
                    tiles.Remove(rt);
                }
                return;
            }

            rt.TryGetStatus(out status);
            if (!tiles.TryGetValue(rt, out var tile))
            {
                tile = CreateTile(rt.Perk);
                tiles[rt] = tile;
            }

            tile.Status = status;
            tile.Group.alpha = rt.InHand ? 1f : stowedAlpha;

            if (status.CountMax > 0 && status.Stacks <= 0) tile.Corner.text = $"{status.Count}/{status.CountMax}";
            else if (status.Stacks > 1) tile.Corner.text = $"x{status.Stacks}";
            else tile.Corner.text = "";

            UpdateTimer(tile);
        }

        // Only the timer bars move per frame.
        private void LateUpdate()
        {
            foreach (var t in tiles.Values) UpdateTimer(t);
        }

        private static void UpdateTimer(Tile t)
        {
            var s = t.Status;
            float pct = 0f;
            if (s.ExpiresAt > 0f && s.Duration > 0f)
                pct = Mathf.Clamp01((s.ExpiresAt - Time.time) / s.Duration);

            var max = t.Timer.anchorMax;
            max.x = pct;
            t.Timer.anchorMax = max;
        }

        private Tile CreateTile(PerkSO perk)
        {
            var root = new GameObject(perk != null ? perk.name : "Perk", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(tileSize, tileSize);

            var bg = root.AddComponent<Image>();
            bg.color = background;
            bg.raycastTarget = false;

            var tile = new Tile { Root = root, Group = root.AddComponent<CanvasGroup>() };
            tile.Group.blocksRaycasts = false;

            if (perk != null && perk.icon != null)
            {
                tile.Icon = Child<Image>(root, "Icon", Vector2.zero, Vector2.one, new Vector2(4f, 6f), new Vector2(-4f, -4f));
                tile.Icon.sprite = perk.icon;
                tile.Icon.preserveAspect = true;
                tile.Icon.raycastTarget = false;
            }
            else
            {
                tile.Label = Text(root, "Label", Vector2.zero, Vector2.one, perk != null ? perk.ShortLabel : "?",
                                  fontSize, TextAlignmentOptions.Center);
            }

            tile.Corner = Text(root, "Corner", new Vector2(0.4f, 0f), new Vector2(1f, 0.45f), "",
                               fontSize * 0.7f, TextAlignmentOptions.BottomRight);

            var timer = Child<Image>(root, "Timer", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 3f));
            timer.color = timerColor;
            timer.raycastTarget = false;
            tile.Timer = timer.rectTransform;

            return tile;
        }

        private TMP_Text Text(GameObject parent, string name, Vector2 min, Vector2 max, string value,
                              float size, TextAlignmentOptions align)
        {
            var t = Child<TextMeshProUGUI>(parent, name, min, max, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            if (font != null) t.font = font;
            t.text = value;
            t.fontSize = size;
            t.color = textColor;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private static T Child<T>(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 offsetMin, Vector2 offsetMax) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go.AddComponent<T>();
        }
    }
}
