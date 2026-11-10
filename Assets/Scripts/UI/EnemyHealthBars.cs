using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Combat.Enemies;

namespace Combat.UI
{
    // Overhead enemy health bars: rank icon and name over a framed bar with scaling ticks, a delayed damage chip,
    // a heal chip the fill climbs to, and a shield layer. Shown while aimed at or recently damaged; bosses excluded.
    [RequireComponent(typeof(RectTransform))]
    public class EnemyHealthBars : MonoBehaviour
    {
        [SerializeField] private UIStyleSO style;
        [Tooltip("Aim ray source. Empty = the main camera.")]
        [SerializeField] private Camera viewCamera;

        [Header("Visibility")]
        [Tooltip("An enemy under the crosshair within this range shows its bar.")]
        [SerializeField] private float aimRange = 150f;
        [SerializeField] private LayerMask aimMask = ~0;
        [Tooltip("Seconds a bar stays after the last aim or hit.")]
        [Min(0f)] [SerializeField] private float holdTime = 3f;
        [Min(0.01f)] [SerializeField] private float fadeTime = 0.4f;
        [Tooltip("Bars beyond this distance are hidden.")]
        [SerializeField] private float maxDistance = 120f;
        [Min(1)] [SerializeField] private int maxBars = 24;

        [Header("Layout")]
        [SerializeField] private float barWidth = 170f;
        [SerializeField] private float barHeight = 10f;
        [SerializeField] private float shieldHeight = 4f;
        [Tooltip("Gap between the frame and the fill, in pixels.")]
        [Min(0f)] [SerializeField] private float framePadding = 2f;
        [SerializeField] private float nameSize = 15f;
        [SerializeField] private float iconSize = 14f;
        [Tooltip("Gap above the head, in metres.")]
        [SerializeField] private float headOffset = 0.35f;
        [Tooltip("Bar scale at max distance; 1 up close. Anything under 1 puts ticks and text off whole pixels, so they shimmer while the enemy moves.")]
        [Range(0.2f, 1f)] [SerializeField] private float farScale = 1f;

        [Header("Look")]
        [SerializeField] private Color frameColor = new Color(0f, 0f, 0f, 0.7f);
        [Range(0, 8)] [SerializeField] private int frameRadius = 2;
        [Tooltip("Light band across the top of the fill.")]
        [SerializeField] private Color sheenColor = new Color(1f, 1f, 1f, 0.18f);
        [Range(0f, 1f)] [SerializeField] private float sheenHeight = 0.4f;
        [Tooltip("Thin rank-coloured line under the name.")]
        [SerializeField] private bool rankUnderline = true;

        [Header("Ticks")]
        [Tooltip("Picks the smallest round step that fits under Max Ticks, so spacing scales with max health (300 = every 10, heavy every 100).")]
        [SerializeField] private bool autoStep = true;
        [Tooltip("Health per small tick when Auto Step is off.")]
        [Min(1f)] [SerializeField] private float tickHealth = 100f;
        [Tooltip("Every Nth tick is a large one.")]
        [Min(1)] [SerializeField] private int majorEvery = 10;
        [SerializeField] private Color tickColor = Color.black;
        [SerializeField] private float tickWidth = 2f;
        [SerializeField] private float majorTickWidth = 3f;
        [Tooltip("Hide ticks past current health, so they only mark the health left.")]
        [SerializeField] private bool ticksOnFillOnly = true;
        [Tooltip("Small tick height as a fraction of the bar, hanging from the top; large ticks are full height.")]
        [Range(0f, 1f)] [SerializeField] private float tickHeight = 0.5f;
        [Tooltip("Most ticks on one bar. Auto Step picks the step that fits; otherwise the step doubles until it fits.")]
        [Min(1)] [SerializeField] private int maxTicks = 30;

        private static readonly float[] NiceSteps = { 1f, 2f, 5f, 10f, 20f, 25f, 50f };

        [Header("Chip")]
        [Tooltip("Damage: the chip holds at the old health this long, then drains down to the fill.")]
        [SerializeField] private float chipDelay = 0.4f;
        [Tooltip("Fraction of the bar per second, for both the damage drain and the heal rise.")]
        [SerializeField] private float chipSpeed = 0.8f;
        [SerializeField] private Color damageChipColor = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("Heal: the chip jumps to the new health in this colour, then the fill rises to meet it.")]
        [SerializeField] private Color healChipColor = new Color(0.45f, 1f, 0.55f, 0.9f);
        [SerializeField] private float healDelay = 0.15f;

        private sealed class Bar
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Icon;
            public RectTransform Pips;
            public Image[] PipImages;
            public TMP_Text Name;
            public Image Underline;
            public RectTransform Chip;
            public Image ChipImage;
            public RectTransform Fill;
            public Image FillImage;
            public RectTransform TickArea;
            public float TickWidthPx;
            public readonly List<Image> Ticks = new List<Image>();
            public readonly List<float> TickAt = new List<float>();
            public float TickedMax = -1f;
            public EnemyRank FilledRank;
            public string FilledName;
            public float TickedScale = -1f;
            public RectTransform ShieldFrame;
            public RectTransform ShieldFill;
            public Image ShieldImage;
        }

        private sealed class Tracked
        {
            public float LastSeen = -999f;
            public float LastHealth = -1f;
            public float Shown;      // fill
            public float Target;     // real health fraction
            public float Chip;
            public float ChipWait;
            public bool Healing;     // heal chip ahead, fill climbing
            public Bar Bar;
        }

        private readonly Dictionary<EnemyDefinitionApplier, Tracked> tracked = new Dictionary<EnemyDefinitionApplier, Tracked>();
        private readonly List<EnemyDefinitionApplier> stale = new List<EnemyDefinitionApplier>();
        private readonly Stack<Bar> free = new Stack<Bar>();
        private RectTransform rect;
        private Canvas canvas;
        private int liveBars;

        private void Awake()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }

        private void OnDisable()
        {
            foreach (var t in tracked.Values) Release(t);
            tracked.Clear();
        }

        private void LateUpdate()
        {
            if (style == null) return;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) return;

            float now = Time.time;
            float dt = Time.deltaTime;
            var aimed = AimedEnemy(cam);

            var active = EnemyDefinitionApplier.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var e = active[i];
                if (e == null || e.Rank == EnemyRank.Boss || e.Health == null) continue;
                if (!tracked.TryGetValue(e, out var t))
                {
                    t = new Tracked();
                    tracked[e] = t;
                }
                if (TrackHealth(e, t)) t.LastSeen = now;
                if (e == aimed) t.LastSeen = now;
                Animate(t, dt);
            }

            stale.Clear();
            foreach (var kv in tracked)
            {
                var e = kv.Key;
                var t = kv.Value;
                if (e == null || !e.isActiveAndEnabled || e.Health == null) { stale.Add(e); continue; }

                float age = now - t.LastSeen;
                bool show = !e.Health.IsDying && age < holdTime + fadeTime;
                if (show && !Place(e, t, cam, age)) show = false;
                if (!show) Release(t);
            }
            foreach (var e in stale)
            {
                Release(tracked[e]);
                tracked.Remove(e);
            }
        }

        private EnemyDefinitionApplier AimedEnemy(Camera cam)
        {
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, aimRange, aimMask, QueryTriggerInteraction.Collide)) return null;
            return hit.collider.GetComponentInParent<EnemyDefinitionApplier>();
        }

        // Same chip rules as the player bar. Returns true when the enemy took damage.
        private bool TrackHealth(EnemyDefinitionApplier e, Tracked t)
        {
            float current = e.Health.CurrentHealth;
            if (Mathf.Approximately(current, t.LastHealth)) return false;

            float max = Mathf.Max(1f, e.Health.MaxHealth);
            float pct = Mathf.Clamp01(current / max);
            bool first = t.LastHealth < 0f;
            bool damaged = !first && current < t.LastHealth;
            t.LastHealth = current;
            t.Target = pct;

            if (first)
            {
                t.Shown = t.Chip = pct;
            }
            else if (pct < t.Shown)
            {
                // Damage: fill drops now; chip keeps the old value, then drains.
                t.Chip = Mathf.Max(t.Chip, t.Shown);
                t.Shown = pct;
                t.ChipWait = chipDelay;
                t.Healing = false;
            }
            else if (pct > t.Shown)
            {
                // Heal: chip jumps ahead; fill rises to meet it. Only the first tick waits.
                t.Chip = pct;
                if (!t.Healing) t.ChipWait = healDelay;
                t.Healing = true;
            }
            return damaged;
        }

        private void Animate(Tracked t, float dt)
        {
            if (Mathf.Approximately(t.Chip, t.Shown)) return;
            if (t.ChipWait > 0f) t.ChipWait -= dt;
            else if (t.Shown < t.Target)
            {
                t.Shown = Mathf.MoveTowards(t.Shown, t.Target, chipSpeed * dt);
                if (Mathf.Approximately(t.Shown, t.Target)) { t.Chip = t.Shown; t.Healing = false; }
            }
            else t.Chip = Mathf.MoveTowards(t.Chip, t.Shown, chipSpeed * dt);
        }

        // Positions and draws the bar; false when it can't be shown (behind the camera, too far, no free bar).
        private bool Place(EnemyDefinitionApplier e, Tracked t, Camera cam, float age)
        {
            Vector3 world = e.HeadPosition + Vector3.up * headOffset;
            float dist = Vector3.Distance(cam.transform.position, world);
            if (dist > maxDistance) return false;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;

            var b = t.Bar;
            if (b == null)
            {
                b = t.Bar = Rent();
                if (b == null) return false;
                Fill(b, e);
            }
            // Definition can land after the bar was handed out (spawned and aimed at the same frame).
            else if (b.FilledRank != e.Rank || b.FilledName != e.DisplayName) Fill(b, e);

            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            // Whole screen pixels for the bar's left edge, so thin ticks and text don't shimmer as it moves.
            float px = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            float halfPx = barWidth * 0.5f * px;
            screen.x = Mathf.Round(screen.x - halfPx) + halfPx;
            screen.y = Mathf.Round(screen.y);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, uiCam, out var local);
            b.Root.anchoredPosition = local;
            b.Root.localScale = Vector3.one * Mathf.Lerp(1f, farScale, Mathf.Clamp01(dist / maxDistance));

            float max = Mathf.Max(1f, e.Health.MaxHealth);
            if (!Mathf.Approximately(max, b.TickedMax) || (canvas != null && !Mathf.Approximately(canvas.scaleFactor, b.TickedScale)))
                BuildTicks(b, max);

            SetWidth(b.Fill, t.Shown);
            SetWidth(b.Chip, t.Chip);
            b.ChipImage.color = t.Healing ? healChipColor : damageChipColor;
            ShowTicks(b, t.Shown);

            var shield = e.Shield;
            bool hasShield = shield != null && shield.MaxShield > 0f;
            b.ShieldFrame.gameObject.SetActive(hasShield);
            if (hasShield)
            {
                SetWidth(b.ShieldFill, Mathf.Clamp01(shield.CurrentShield / shield.MaxShield));
                var sc = shield.ShieldColor;
                b.ShieldImage.color = sc.a > 0f ? sc : style.enemyShield;
            }

            b.Group.alpha = age <= holdTime ? 1f : 1f - Mathf.Clamp01((age - holdTime) / fadeTime);
            return true;
        }

        // Name, rank colour and icon; set once when a bar is handed to an enemy.
        private void Fill(Bar b, EnemyDefinitionApplier e)
        {
            var color = style.RankColor(e.Rank);
            b.Name.text = e.DisplayName;
            b.FilledRank = e.Rank;
            b.FilledName = e.DisplayName;
            b.FillImage.color = color;
            b.Underline.color = new Color(color.r, color.g, color.b, 0.8f);
            b.Underline.gameObject.SetActive(rankUnderline);
            b.TickedMax = -1f;

            var icon = style.RankIcon(e.Rank);
            b.Icon.gameObject.SetActive(icon != null);
            b.Pips.gameObject.SetActive(icon == null);
            if (icon != null)
            {
                b.Icon.sprite = icon;
                b.Icon.color = color;
                return;
            }
            int count = Mathf.Clamp((int)e.Rank + 1, 1, b.PipImages.Length);
            for (int i = 0; i < b.PipImages.Length; i++)
            {
                b.PipImages[i].gameObject.SetActive(i < count);
                b.PipImages[i].color = color;
            }
        }

        private static void SetWidth(RectTransform rt, float frac)
        {
            var max = rt.anchorMax;
            max.x = frac;
            rt.anchorMax = max;
        }

        // Same tick rules as the player bar; positions snap to whole screen pixels.
        private void BuildTicks(Bar b, float max)
        {
            b.TickedMax = max;
            b.TickAt.Clear();
            float width = b.TickWidthPx;
            float px = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            b.TickedScale = canvas != null ? canvas.scaleFactor : 1f;

            float step = tickHealth;
            int every = majorEvery;
            if (autoStep) step = AutoStep(max);
            else while (max / step > maxTicks) { step *= 2f; every = Mathf.Max(1, every / 2); }

            int count = Mathf.FloorToInt((max - 0.001f) / step);
            for (int i = 0; i < count; i++)
            {
                var img = i < b.Ticks.Count ? b.Ticks[i] : NewTick(b);
                bool major = (i + 1) % every == 0;
                float frac = (i + 1) * step / max;
                float w = Mathf.Max(1f, Mathf.Round((major ? majorTickWidth : tickWidth) * px)) / px;

                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0f, major ? 0f : 1f - tickHeight);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(w, 0f);
                rt.anchoredPosition = new Vector2(Mathf.Round((frac * width - w * 0.5f) * px) / px, 0f);
                img.color = tickColor;
                b.TickAt.Add(frac);
            }
            for (int i = count; i < b.Ticks.Count; i++) b.Ticks[i].gameObject.SetActive(false);
        }

        // 1, 2, 5, 10, 20, 25, 50, then the same pattern times 10, 100, ...
        private float AutoStep(float max)
        {
            for (float scale = 1f; scale < 1e7f; scale *= 10f)
                foreach (float n in NiceSteps)
                {
                    float step = n * scale;
                    if (max / step <= maxTicks) return step;
                }
            return max;
        }

        private void ShowTicks(Bar b, float shown)
        {
            for (int i = 0; i < b.TickAt.Count; i++)
                b.Ticks[i].gameObject.SetActive(!ticksOnFillOnly || b.TickAt[i] < shown);
        }

        private Image NewTick(Bar b)
        {
            var img = UIKit.Solid(b.TickArea, "Tick", tickColor);
            img.rectTransform.pivot = new Vector2(0f, 1f);
            b.Ticks.Add(img);
            return img;
        }

        private Bar Rent()
        {
            Bar b;
            if (free.Count > 0) b = free.Pop();
            else
            {
                if (liveBars >= maxBars) return null;
                b = Build();
                liveBars++;
            }
            b.Root.gameObject.SetActive(true);
            return b;
        }

        private void Release(Tracked t)
        {
            if (t.Bar == null) return;
            t.Bar.Root.gameObject.SetActive(false);
            free.Push(t.Bar);
            t.Bar = null;
        }

        // Bottom-centre pivot: name row on top, shield frame, then the health frame at the bottom.
        private Bar Build()
        {
            var b = new Bar();
            float nameRow = UIKit.LineHeight(nameSize);
            float healthFrame = barHeight + framePadding * 2f;
            float shieldFrame = shieldHeight + framePadding * 2f;

            b.Root = UIKit.Node(rect, "EnemyBar");
            b.Root.anchorMin = b.Root.anchorMax = new Vector2(0.5f, 0.5f);
            b.Root.pivot = new Vector2(0.5f, 0f);
            b.Root.sizeDelta = new Vector2(barWidth, nameRow + 3f + shieldFrame + 1f + healthFrame);
            b.Group = b.Root.gameObject.AddComponent<CanvasGroup>();
            b.Group.blocksRaycasts = false;
            b.Group.interactable = false;

            // Name row: rank icon or pips, then the name.
            var header = UIKit.Anchor(UIKit.Node(b.Root, "Header"), new Vector2(0f, 1f), new Vector2(1f, 1f),
                                      new Vector2(0f, -nameRow), Vector2.zero);
            UIKit.HStack(header, 5f, null, TextAnchor.MiddleLeft);

            b.Icon = UIKit.Solid(header, "RankIcon", Color.white);
            b.Icon.preserveAspect = true;
            UIKit.Size(b.Icon, iconSize, iconSize);

            b.Pips = UIKit.Node(header, "RankPips");
            UIKit.HStack(b.Pips, 2f, null, TextAnchor.MiddleLeft);
            UIKit.Size(b.Pips, height: iconSize);
            b.PipImages = new Image[4];
            for (int i = 0; i < b.PipImages.Length; i++)
            {
                b.PipImages[i] = UIKit.Solid(b.Pips, "Pip", Color.white);
                UIKit.Size(b.PipImages[i], 3f, iconSize * 0.7f);
            }

            b.Name = UIKit.Text(header, "Name", style, "", nameSize, style.text, TextAlignmentOptions.MidlineLeft, heading: true);
            b.Name.characterSpacing = style.headingSpacing * 0.25f;
            UIKit.Size(b.Name, flexWidth: 1f);

            b.Underline = UIKit.Solid(b.Root, "Underline", Color.white);
            UIKit.Anchor(b.Underline.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                         new Vector2(0f, -nameRow - 2f), new Vector2(0f, -nameRow - 1f));

            // Health: frame, track, chip, fill, sheen, ticks.
            var frame = UIKit.Box(b.Root, "HealthFrame", style, frameColor);
            frame.sprite = style.FillSprite(frameRadius);
            UIKit.Anchor(frame.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, healthFrame));

            var area = UIKit.Solid(frame.rectTransform, "Track", style.barTrack).rectTransform;
            UIKit.Stretch(area, framePadding, framePadding, framePadding, framePadding);
            b.TickWidthPx = barWidth - framePadding * 2f;

            b.ChipImage = UIKit.Solid(area, "Chip", damageChipColor);
            b.Chip = UIKit.Stretch(b.ChipImage.rectTransform);
            b.FillImage = UIKit.Solid(area, "Fill", Color.white);
            b.Fill = UIKit.Stretch(b.FillImage.rectTransform);
            var sheen = UIKit.Solid(b.Fill, "Sheen", sheenColor);
            UIKit.Anchor(sheen.rectTransform, new Vector2(0f, 1f - sheenHeight), Vector2.one, Vector2.zero, Vector2.zero);
            b.TickArea = UIKit.Stretch(UIKit.Node(area, "Ticks"));

            // Shield: same frame style, above health; hidden until an enemy has one.
            var sFrame = UIKit.Box(b.Root, "ShieldFrame", style, frameColor);
            sFrame.sprite = style.FillSprite(frameRadius);
            b.ShieldFrame = sFrame.rectTransform;
            UIKit.Anchor(b.ShieldFrame, Vector2.zero, new Vector2(1f, 0f),
                         new Vector2(0f, healthFrame + 1f), new Vector2(0f, healthFrame + 1f + shieldFrame));
            var sArea = UIKit.Solid(b.ShieldFrame, "Track", style.barTrack).rectTransform;
            UIKit.Stretch(sArea, framePadding, framePadding, framePadding, framePadding);
            b.ShieldImage = UIKit.Solid(sArea, "Shield", style.enemyShield);
            b.ShieldFill = UIKit.Stretch(b.ShieldImage.rectTransform);
            b.ShieldFrame.gameObject.SetActive(false);

            b.Root.gameObject.SetActive(false);
            return b;
        }
    }
}
