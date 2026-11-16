using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Combat.Stats;
using Combat.Info;

namespace Combat.UI
{
    // Stat breakdown or keyword popup that follows the cursor. Click to pin it in place, click again to release.
    [RequireComponent(typeof(RectTransform))]
    public class StatTooltip : MonoBehaviour
    {
        [SerializeField] private Vector2 cursorOffset = new Vector2(22f, -22f);
        [SerializeField] private float width = 380f;
        [SerializeField] private float edgeMargin = 12f;

        private static readonly string[] BucketOrder = { StatResolver.FLAT, StatResolver.ADDITIVE, StatResolver.MULTIPLICATIVE };

        private readonly List<StatBreakdownLine> lines = new List<StatBreakdownLine>();
        private InventoryScreen screen;
        private StatSheetSO.Entry entry;
        private RectTransform box;
        private int builtVersion = int.MinValue;
        private bool builtPinned;
        private KeywordLink keyword;
        private DescribeContext keywordContext;
        private bool hasKeyword;

        public bool IsPinned { get; private set; }

        private void Awake()
        {
            var rt = (RectTransform)transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        private void OnDisable() => Hide();

        public void Hover(InventoryScreen owner, StatSheetSO.Entry e, bool on)
        {
            if (IsPinned || e == null) return;
            if (on) Show(owner, e);
            else if (entry == e) Hide();
        }

        public void TogglePin(InventoryScreen owner, StatSheetSO.Entry e)
        {
            if (e == null) return;
            if (IsPinned && entry == e) { IsPinned = false; return; }
            if (entry != e) Show(owner, e);
            IsPinned = true;
        }

        public void HoverKeyword(InventoryScreen owner, KeywordLink link, DescribeContext ctx, bool on)
        {
            if (IsPinned || link.Keyword == null) return;
            if (on) ShowKeyword(owner, link, ctx);
            else if (SameKeyword(link)) Hide();
        }

        public void ToggleKeywordPin(InventoryScreen owner, KeywordLink link, DescribeContext ctx)
        {
            if (link.Keyword == null) return;
            if (IsPinned && SameKeyword(link)) { IsPinned = false; return; }
            if (!SameKeyword(link)) ShowKeyword(owner, link, ctx);
            IsPinned = true;
        }

        private bool SameKeyword(KeywordLink link) => hasKeyword && keyword.Keyword == link.Keyword && keyword.Detail == link.Detail;

        private void ShowKeyword(InventoryScreen owner, KeywordLink link, DescribeContext ctx)
        {
            screen = owner;
            entry = null;
            keyword = link;
            keywordContext = ctx;
            hasKeyword = true;
            builtVersion = int.MinValue;
        }

        public void Hide()
        {
            IsPinned = false;
            hasKeyword = false;
            entry = null;
            Kill(box);
            box = null;
        }

        // Hidden now, destroyed at frame end. Scale, not SetActive: this can run while the menu is deactivating.
        private static void Kill(RectTransform rt)
        {
            if (rt == null) return;
            rt.localScale = Vector3.zero;
            UIKit.Destroy(rt.gameObject);
        }

        private void Show(InventoryScreen owner, StatSheetSO.Entry e)
        {
            screen = owner;
            entry = e;
            hasKeyword = false;
            builtVersion = int.MinValue;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || screen == null) return;
            if (hasKeyword)
            {
                if (screen.Style == null) { Hide(); return; }
                bool shown = box != null;
                if (!shown || builtVersion == int.MinValue || builtPinned != IsPinned)
                {
                    BuildKeyword(screen.Style);
                    builtVersion = 0;
                    builtPinned = IsPinned;
                }
                if (!IsPinned || !shown) Place();
                return;
            }
            if (entry == null) return;

            var stats = screen.PlayerStats;
            var c = stats != null ? stats.Container : null;
            if (c == null || entry.stat == null || screen.Style == null) { Hide(); return; }

            bool wasShown = box != null;
            int v = c.GetVersion(entry.stat);
            if (!wasShown || v != builtVersion || builtPinned != IsPinned)
            {
                Build(c, screen.Style);
                builtVersion = v;
                builtPinned = IsPinned;
            }
            if (!IsPinned || !wasShown) Place();
        }

        // ------------------------------------------------------------------ build

        private void Build(StatContainer c, UIStyleSO s)
        {
            Vector2 keep = box != null ? box.anchoredPosition : Vector2.zero;
            Kill(box);

            var stat = entry.stat;
            float baseValue = c.GetBreakdown(stat, lines);
            float final = c.Resolve(stat);

            var bg = UIKit.Box(transform, "Box", s, s.panel, true, IsPinned ? s.accent : s.panelBorder);
            box = bg.rectTransform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0f, 1f);
            box.sizeDelta = new Vector2(width, 0f);
            box.anchoredPosition = keep;
            UIKit.VStack(bg, 6f, UIKit.Pad(s.padding * 0.75f));
            var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = UIKit.Node(box, "Head");
            UIKit.HStack(head, 10f);
            UIKit.Size(UIKit.Text(head, "Name", s, entry.Label, s.headingSize + 1f, s.text, heading: true), flexWidth: 1f);
            UIKit.Text(head, "Final", s, StatFormat.Value(entry, final), s.bodySize + 3f, s.text, TextAlignmentOptions.MidlineRight, role: TextRole.Number);

            Row(box, s, "Base", StatFormat.Value(entry, baseValue), s.textMuted);

            bool any = false;
            foreach (var bucket in BucketOrder) any |= Bucket(box, s, bucket);
            any |= OtherBuckets(box, s);
            if (!any) UIKit.Text(box, "None", s, "No modifiers.", s.smallSize, s.textFaint);

            Rule(box, s);
            UIKit.Text(box, "Formula", s, Formula(stat), s.smallSize - 2f, s.textFaint, wrap: true);
            if (IsPinned) UIKit.Label(box, "Pinned", s, "Pinned  -  click the stat to release", s.accent);

            LayoutRebuilder.ForceRebuildLayoutImmediate(box);
        }

        // Keyword name, its general rule, then the numbers for the source that linked it.
        private void BuildKeyword(UIStyleSO s)
        {
            Vector2 keep = box != null ? box.anchoredPosition : Vector2.zero;
            Kill(box);

            var k = keyword.Keyword;
            var edge = new Color(k.color.r, k.color.g, k.color.b, 0.6f);
            var bg = UIKit.Box(transform, "Box", s, s.panel, true, IsPinned ? s.accent : edge);
            box = bg.rectTransform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0f, 1f);
            box.sizeDelta = new Vector2(width, 0f);
            box.anchoredPosition = keep;
            UIKit.VStack(bg, 6f, UIKit.Pad(s.padding * 0.75f));
            var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UIKit.Text(box, "Name", s, k.Name, s.headingSize + 2f, k.color, heading: true);
            if (!string.IsNullOrWhiteSpace(k.description))
                UIKit.Text(box, "Rule", s, k.description, s.smallSize, s.textMuted, wrap: true);

            if (keyword.Detail != null)
            {
                string detail = Describer.Build(keyword.Detail, keywordContext);
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    Rule(box, s);
                    UIKit.Text(box, "Detail", s, detail, s.smallSize, s.text, wrap: true).richText = true;
                }
            }
            if (IsPinned) UIKit.Label(box, "Pinned", s, "Pinned  -  click the word to release", s.accent);

            LayoutRebuilder.ForceRebuildLayoutImmediate(box);
        }

        private bool Bucket(RectTransform parent, UIStyleSO s, string bucket)
        {
            bool header = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.BucketId != bucket) continue;
                if (!header) { UIKit.Label(parent, "Bucket", s, BucketName(bucket), s.textFaint); header = true; }
                Line(parent, s, l);
            }
            return header;
        }

        // Custom-tree bucket ids, if any.
        private bool OtherBuckets(RectTransform parent, UIStyleSO s)
        {
            bool any = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (System.Array.IndexOf(BucketOrder, l.BucketId) >= 0) continue;
                if (!any) { UIKit.Label(parent, "Bucket", s, "Other", s.textFaint); any = true; }
                Line(parent, s, l);
            }
            return any;
        }

        private void Line(RectTransform parent, UIStyleSO s, StatBreakdownLine l)
        {
            string source = StatSource.Label(l.Owner);
            if (l.IsDerived && l.SourceStat != null) source += "  (from " + StatName(l.SourceStat) + ")";
            Color color = Mathf.Abs(l.Value) < 0.00001f ? s.textMuted : (l.Value > 0f != entry.LowerIsBetter) ? s.positive : s.negative;
            Row(parent, s, source, Amount(l.BucketId, l.Value), color);
        }

        private static void Row(RectTransform parent, UIStyleSO s, string label, string value, Color valueColor)
        {
            var row = UIKit.Node(parent, "Row");
            UIKit.HStack(row, 10f);
            UIKit.Size(UIKit.Text(row, "Label", s, label, s.smallSize, s.textMuted), flexWidth: 1f);
            UIKit.Text(row, "Value", s, value, s.smallSize, valueColor, TextAlignmentOptions.MidlineRight, role: TextRole.Number);
        }

        private static void Rule(RectTransform parent, UIStyleSO s)
        {
            var wrap = UIKit.Node(parent, "Rule");
            UIKit.Size(wrap, height: 9f);
            var line = UIKit.Solid(wrap, "Line", s.divider);
            UIKit.Anchor(line.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -0.5f), new Vector2(0f, 0.5f));
        }

        // ------------------------------------------------------------------ text

        private string BucketName(string bucket)
        {
            if (bucket == StatResolver.FLAT) return "Flat";
            if (bucket == StatResolver.MULTIPLICATIVE) return "Multipliers";
            return entry.stat.resolutionMode == StatResolutionMode.ScaledBase ? "Increased" : "Added";
        }

        private string Amount(string bucket, float v)
        {
            string sign = v < 0f ? "-" : "+";
            if (bucket == StatResolver.MULTIPLICATIVE) return "x" + (1f + v).ToString("0.##");
            if (bucket == StatResolver.ADDITIVE && entry.stat.resolutionMode == StatResolutionMode.ScaledBase)
                return sign + (Mathf.Abs(v) * 100f).ToString("0.#") + "%";
            return sign + StatFormat.Value(entry.stat, Mathf.Abs(v), entry.unit, entry.decimals);
        }

        private static string Formula(StatDefinitionSO stat)
        {
            string f = stat.resolutionMode == StatResolutionMode.Pool
                ? "Adds base, flat and added values, then applies each multiplier."
                : "Base plus flat, scaled by total increase, then each multiplier.";
            if (stat.useMin || stat.useMax) f += " Clamped to its limits.";
            return f;
        }

        private static string StatName(StatDefinitionSO stat)
            => string.IsNullOrWhiteSpace(stat.displayName) ? stat.name : stat.displayName;

        // ------------------------------------------------------------------ placement

        private void Place()
        {
            if (box == null) return;
            var rt = (RectTransform)transform;
            var input = screen.Input;
            Vector2 point = input != null ? input.UI.Point.ReadValue<Vector2>() : Vector2.zero;

            var canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, point, cam, out var local)) return;

            Vector2 size = box.rect.size;
            Rect r = rt.rect;

            float x = local.x + cursorOffset.x;
            if (x + size.x > r.xMax - edgeMargin) x = local.x - cursorOffset.x - size.x;
            float y = local.y + cursorOffset.y;
            if (y - size.y < r.yMin + edgeMargin) y = local.y - cursorOffset.y + size.y;

            x = Mathf.Clamp(x, r.xMin + edgeMargin, Mathf.Max(r.xMin + edgeMargin, r.xMax - edgeMargin - size.x));
            y = Mathf.Clamp(y, Mathf.Min(r.yMax - edgeMargin, r.yMin + edgeMargin + size.y), r.yMax - edgeMargin);
            box.anchoredPosition = new Vector2(x, y);
        }
    }
}
