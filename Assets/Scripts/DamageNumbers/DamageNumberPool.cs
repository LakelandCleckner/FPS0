using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Combat.Core;

namespace Combat.Feedback
{
    // Pools floating damage numbers. Diablo-style RELATIVE sizing: a number's
    // size comes from its log-distance to the decaying DamageMagnitudeReference,
    // so the biggest recent hits pop and chip damage recedes, recalibrating as
    // the player's damage range shifts. Lifetime scales with that same relative
    // magnitude (bigger = lingers longer), and bigger numbers rise slower so
    // they hang near the enemy. Size/lifetime/rise all locked at spawn.
    //
    // Font, style (bold=crit, italic=debuff), and color gradient come from the
    // DamageTypeSO's 2x2 (crit x debuff) state — so each type owns its own look
    // and modifier states.
    public class DamageNumberPool : MonoBehaviour
    {
        public static DamageNumberPool Instance { get; private set; }

        [Header("Prefab & Pool")]
        [SerializeField] private DamageNumber numberPrefab;
        [SerializeField] private int initialSize = 40;

        [Header("Relative Sizing")]
        [Tooltip("Log-distance from the reference that maps to the SMALLEST size.")]
        [SerializeField] private float minLogDistance = -0.69f; // ~0.5x typical
        [Tooltip("Log-distance from the reference that maps to the LARGEST size.")]
        [SerializeField] private float maxLogDistance = 0.69f;  // ~2x typical
        [SerializeField] private float minSize = 2.5f;
        [SerializeField] private float maxSize = 12f;

        [Header("Relative Lifetime")]
        [SerializeField] private float minLifetime = 0.6f;
        [SerializeField] private float maxLifetime = 2.5f;

        [Header("Motion")]
        [Tooltip("Rise speed for the smallest numbers; bigger ones rise slower.")]
        [SerializeField] private float baseRiseSpeed = 1.5f;
        [Tooltip("How much bigger numbers slow their rise (0 = none, 1 = strong).")]
        [Range(0f, 1f)] [SerializeField] private float riseSlowdown = 0.6f;
        [SerializeField] private float horizontalDrift = 0.3f;

        // Fonts now come from the damage type, not the pool. (Kept here only as a
        // fallback if a type has no font assigned.)
        [Header("Fallback Font")]
        [SerializeField] private TMP_FontAsset fallbackFont;

        private readonly Queue<DamageNumber> available = new Queue<DamageNumber>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            for (int i = 0; i < initialSize; i++)
                available.Enqueue(CreateOne());
        }

        private DamageNumber CreateOne()
        {
            var n = Instantiate(numberPrefab, transform);
            n.gameObject.SetActive(false);
            return n;
        }

        public void Return(DamageNumber n) => available.Enqueue(n);

        // Spawn a number. The damage type supplies font/gradient/style for the
        // current crit/debuff combination.
        public void Spawn(Vector3 worldPos, float amount, DamageTypeSO type, bool isCrit, bool isDebuffed)
        {
            var n = available.Count > 0 ? available.Dequeue() : CreateOne();

            // Relative magnitude: where does this hit sit vs recent typical damage?
            float logDist = DamageMagnitudeReference.Instance != null
                ? DamageMagnitudeReference.Instance.GetLogDistance(amount)
                : 0f;
            float magT = Mathf.InverseLerp(minLogDistance, maxLogDistance, logDist);

            float size = Mathf.Lerp(minSize, maxSize, magT);
            float life = Mathf.Lerp(minLifetime, maxLifetime, magT);
            float rise = baseRiseSpeed * (1f - riseSlowdown * magT);

            // feed the reference AFTER computing, so a hit sizes against the
            // range it arrived into, not one it just shifted
            if (DamageMagnitudeReference.Instance != null)
                DamageMagnitudeReference.Instance.Report(amount);

            // type-driven look (font / gradient / style) for this crit/debuff state
            VertexGradient gradient;
            TMP_FontAsset font;
            FontStyles style;
            if (type != null)
            {
                gradient = type.GetGradient(isCrit, isDebuffed);
                font = type.font != null ? type.font : fallbackFont;
                style = type.GetStyle(isCrit, isDebuffed);
            }
            else
            {
                gradient = new VertexGradient(Color.white);
                font = fallbackFont;
                style = FontStyles.Normal;
            }

            n.Activate(
                pool: this,
                worldPos: worldPos,
                content: DamageNumberFormatter.Format(amount),
                gradient: gradient,
                font: font,
                style: style,
                lifetime: life,
                riseSpeed: rise,
                horizontalDrift: horizontalDrift,
                fontSize: size);
        }
    }
}