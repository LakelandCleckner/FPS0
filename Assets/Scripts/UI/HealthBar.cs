using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Combat.Loadout;

namespace Combat.UI
{
    // Segmented player health bar: two sliders (fill over a delayed chip), tick marks every N health
    // with heavier ticks every M, current / max text, low-health pulse, and floating hit/heal numbers.
    public class HealthBar : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Found on the player if empty.")]
        [SerializeField] private CombatantHealth health;

        [Header("Parts")]
        [Tooltip("Health. Its Fill image is coloured by Normal/Low Color.")]
        [SerializeField] private Slider fill;
        [Tooltip("Behind Fill. Shows recent damage before draining.")]
        [SerializeField] private Slider chip;
        [Tooltip("Any empty RectTransform. It's moved into Fill's Fill Area at start so ticks line up with the fill exactly.")]
        [SerializeField] private RectTransform tickArea;
        [SerializeField] private TMP_Text label;
        [Tooltip("Where hit numbers spawn. Empty = this object.")]
        [SerializeField] private RectTransform popupAnchor;

        [Header("Look")]
        [Tooltip("Gap between the bar's background and the fill, in pixels: the dark frame.")]
        [Min(0f)][SerializeField] private float framePadding = 2f;
        [SerializeField] private Color normalColor = new Color(0.32f, 0.78f, 0.36f);
        [SerializeField] private Color lowColor = new Color(0.9f, 0.25f, 0.2f);
        [Range(0f, 1f)][SerializeField] private float lowThreshold = 0.3f;
        [SerializeField] private float pulseSpeed = 4f;

        [Header("Ticks")]
        [Tooltip("Health per small tick.")]
        [Min(1f)][SerializeField] private float tickHealth = 100f;
        [Tooltip("Every Nth tick is a large one (10 = every 1000 at 100 per tick).")]
        [Min(1)][SerializeField] private int majorEvery = 10;
        [SerializeField] private Color tickColor = Color.black;
        [SerializeField] private float tickWidth = 2f;
        [SerializeField] private float majorTickWidth = 3f;
        [Tooltip("Hide ticks past current health, so they only mark the health you have.")]
        [SerializeField] private bool ticksOnFillOnly = true;
        [Tooltip("Small tick height as a fraction of the bar, hanging from the top; large ticks are full height.")]
        [Range(0f, 1f)][SerializeField] private float tickHeight = 0.5f;
        [Tooltip("Above this many ticks, the step doubles until it fits.")]
        [Min(1)][SerializeField] private int maxTicks = 60;

        [Header("Chip")]
        [Tooltip("Damage: the chip holds at the old health this long, then drains down to the fill.")]
        [SerializeField] private float chipDelay = 0.4f;
        [Tooltip("Fraction of the bar per second, for both the damage drain and the heal rise.")]
        [SerializeField] private float chipSpeed = 0.8f;
        [SerializeField] private Color damageChipColor = new Color(1f, 1f, 1f, 0.85f);
        [Tooltip("Heal: the chip jumps to the new health in this colour, then the fill rises to meet it.")]
        [SerializeField] private Color healChipColor = new Color(0.45f, 1f, 0.55f, 0.9f);
        [SerializeField] private float healDelay = 0.15f;

        [Header("Hit numbers")]
        [SerializeField] private Color damageColor = new Color(1f, 0.35f, 0.3f);
        [SerializeField] private Color healColor = new Color(0.45f, 1f, 0.55f);
        [SerializeField] private float popupRise = 40f;
        [SerializeField] private float popupLife = 0.8f;
        [Tooltip("Changes smaller than this don't spawn a number.")]
        [SerializeField] private float popupMin = 0.5f;

        private sealed class Popup
        {
            public TMP_Text Text;
            public float Age;
            public Vector2 Start;
        }

        private readonly List<Popup> live = new List<Popup>();
        private readonly Stack<Popup> pool = new Stack<Popup>();

        private float shown;       // fill
        private float target;      // real health fraction
        private float chipValue;
        private Graphic chipGraphic;
        private float chipWait;
        private float lastHealth = -1f;
        private float tickedMax = -1f;
        private float tickedWidth = -1f;
        private float tickedScale = -1f;
        private readonly List<float> tickAt = new List<float>();
        private bool low;
        private Graphic fillGraphic;
        private Canvas canvas;
        private readonly List<Image> ticks = new List<Image>();

        private void Awake()
        {
            if (health == null)
            {
                var player = FindFirstObjectByType<PlayerLoadout>();
                if (player != null) health = player.GetComponentInParent<CombatantHealth>();
                if (health == null && player != null) health = player.GetComponentInChildren<CombatantHealth>();
            }
            if (health == null) Debug.LogWarning("[HealthBar] No player CombatantHealth found.");
            if (popupAnchor == null) popupAnchor = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();

            Setup(fill);
            Setup(chip);
            if (fill != null && fill.fillRect != null) fillGraphic = fill.fillRect.GetComponent<Graphic>();
            if (chip != null && chip.fillRect != null) chipGraphic = chip.fillRect.GetComponent<Graphic>();

            // Ticks share the fill's exact rectangle, whatever the slider defaults were.
            if (tickArea != null && fill != null && fill.fillRect != null)
            {
                tickArea.SetParent(fill.fillRect.parent, false);
                Stretch(tickArea, 0f);
                tickArea.SetAsLastSibling();
            }
        }

        // Display-only sliders, and undo the default slider layout (half-height track, offsets for a handle).
        private void Setup(Slider s)
        {
            if (s == null) return;
            s.minValue = 0f;
            s.maxValue = 1f;
            s.wholeNumbers = false;
            s.interactable = false;
            s.transition = Selectable.Transition.None;

            var bg = s.transform.Find("Background") as RectTransform;
            if (bg != null) Stretch(bg, 0f);

            var fillRect = s.fillRect;
            if (fillRect == null) return;

            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var area = fillRect.parent as RectTransform;
            if (area != null) Stretch(area, framePadding);
        }

        private static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        private void OnEnable()
        {
            if (health == null) return;
            health.HealthChanged += HandleHealth;
            if (health.MaxHealth > 0f && health.CurrentHealth > 0f) HandleHealth(health.CurrentHealth, health.MaxHealth);
        }

        private void OnDisable()
        {
            if (health != null) health.HealthChanged -= HandleHealth;
        }

        private void HandleHealth(float current, float max)
        {
            float pct = max > 0f ? Mathf.Clamp01(current / max) : 0f;

            if (lastHealth >= 0f)
            {
                float delta = current - lastHealth;
                if (Mathf.Abs(delta) >= popupMin) SpawnPopup(delta);
            }
            lastHealth = current;

            bool first = target <= 0f && shown <= 0f;
            target = pct;

            if (first)
            {
                shown = chipValue = pct;
            }
            else if (pct < shown)
            {
                // Damage: fill drops now; chip keeps the old value, then drains.
                chipValue = Mathf.Max(chipValue, shown);
                shown = pct;
                chipWait = chipDelay;
                SetChipColor(damageChipColor);
            }
            else if (pct > shown)
            {
                // Heal: chip jumps ahead in green; fill rises to meet it.
                chipValue = pct;
                chipWait = healDelay;
                SetChipColor(healChipColor);
            }

            low = pct > 0f && pct <= lowThreshold;

            if (fill != null) fill.SetValueWithoutNotify(shown);
            if (chip != null) chip.SetValueWithoutNotify(chipValue);
            if (!Mathf.Approximately(max, tickedMax)) BuildTicks(max);
            ShowTicks();
            if (label != null) label.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
            if (fillGraphic != null && !low) fillGraphic.color = normalColor;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Layout can resize the bar after the first build; ticks are placed in pixels.
            if (tickArea != null && tickedMax > 0f &&
                (!Mathf.Approximately(tickArea.rect.width, tickedWidth) || (canvas != null && !Mathf.Approximately(canvas.scaleFactor, tickedScale))))
                BuildTicks(tickedMax);

            if (!Mathf.Approximately(chipValue, shown))
            {
                if (chipWait > 0f) chipWait -= dt;
                else if (shown < target)
                {
                    // Heal: the fill climbs up to the green chip.
                    shown = Mathf.MoveTowards(shown, target, chipSpeed * dt);
                    if (fill != null) fill.SetValueWithoutNotify(shown);
                    ShowTicks();
                    if (Mathf.Approximately(shown, target)) chipValue = shown;
                }
                else
                {
                    // Damage: the chip drains down to the fill.
                    chipValue = Mathf.MoveTowards(chipValue, shown, chipSpeed * dt);
                    if (chip != null) chip.SetValueWithoutNotify(chipValue);
                }
            }

            if (low && fillGraphic != null)
            {
                float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed);
                fillGraphic.color = Color.Lerp(lowColor, normalColor, t * 0.5f);
            }

            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                p.Age += dt;
                float t = p.Age / popupLife;
                if (t >= 1f)
                {
                    p.Text.gameObject.SetActive(false);
                    live.RemoveAt(i);
                    pool.Push(p);
                    continue;
                }
                p.Text.rectTransform.anchoredPosition = p.Start + Vector2.up * popupRise * t;
                var c = p.Text.color;
                c.a = 1f - t * t;
                p.Text.color = c;
            }
        }

        private void SetChipColor(Color c)
        {
            if (chipGraphic != null) chipGraphic.color = c;
        }

        private void SpawnPopup(float delta)
        {
            var p = pool.Count > 0 ? pool.Pop() : CreatePopup();
            p.Age = 0f;
            p.Start = new Vector2(Random.Range(-8f, 8f), 0f);
            p.Text.text = delta < 0f ? $"-{Mathf.CeilToInt(-delta)}" : $"+{Mathf.CeilToInt(delta)}";
            p.Text.color = delta < 0f ? damageColor : healColor;
            p.Text.rectTransform.anchoredPosition = p.Start;
            p.Text.gameObject.SetActive(true);
            live.Add(p);
        }

        private Popup CreatePopup()
        {
            var go = new GameObject("HitNumber", typeof(RectTransform));
            go.transform.SetParent(popupAnchor, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (label != null)
            {
                text.font = label.font;
                text.fontSize = label.fontSize;
            }
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(120f, 40f);

            return new Popup { Text = text };
        }

        // Rebuilt when max health or the bar's width changes. Positions snap to whole pixels.
        private void BuildTicks(float max)
        {
            tickedMax = max;
            tickAt.Clear();
            if (tickArea == null || max <= 0f) return;

            float width = tickArea.rect.width;
            tickedWidth = width;

            // Snap to real screen pixels, not canvas units, so a scaled canvas can't make ticks uneven.
            float px = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            tickedScale = canvas != null ? canvas.scaleFactor : 1f;

            float step = tickHealth;
            int every = majorEvery;
            while (max / step > maxTicks) { step *= 2f; every = Mathf.Max(1, every / 2); }

            int count = Mathf.FloorToInt((max - 0.001f) / step);
            for (int i = 0; i < count; i++)
            {
                var img = i < ticks.Count ? ticks[i] : NewTick();
                bool major = (i + 1) % every == 0;
                float frac = (i + 1) * step / max;
                float w = Mathf.Max(1f, Mathf.Round((major ? majorTickWidth : tickWidth) * px)) / px;

                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0f, major ? 0f : 1f - tickHeight);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(w, 0f);
                rt.anchoredPosition = new Vector2(Mathf.Round((frac * width - w * 0.5f) * px) / px, 0f);
                img.color = tickColor;

                tickAt.Add(frac);
            }
            for (int i = count; i < ticks.Count; i++) ticks[i].gameObject.SetActive(false);
            ShowTicks();
        }

        private void ShowTicks()
        {
            for (int i = 0; i < tickAt.Count; i++)
                ticks[i].gameObject.SetActive(!ticksOnFillOnly || tickAt[i] < shown);
        }

        private Image NewTick()
        {
            var go = new GameObject("Tick", typeof(RectTransform));
            go.transform.SetParent(tickArea, false);
            var img = go.AddComponent<Image>();
            img.color = tickColor;
            img.raycastTarget = false;
            img.rectTransform.pivot = new Vector2(0f, 1f);
            ticks.Add(img);
            return img;
        }
    }
}