using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Combat.Feedback
{
    // Pools floating damage numbers. RELATIVE sizing: a number's
    // size comes from its log-distance to the decaying DamageMagnitudeReference,
    // so the biggest recent hits pop and chip damage recedes, recalibrating as
    // the player's damage range shifts. Lifetime scales with that same relative
    // magnitude (bigger = lingers longer), and bigger numbers rise slower so
    // they hang near the enemy. Size/lifetime/rise all locked at spawn. Crits
    // swap the font only — size comes from relative magnitude, not a flat jump.
    public class DamageNumberPool : MonoBehaviour
    {
        public static DamageNumberPool Instance { get; private set; }

        [Header("Prefab & Pool")]
        [SerializeField] private DamageNumber numberPrefab;
        [SerializeField] private int initialSize = 40;

        [Header("Relative Sizing")]
        [Tooltip("Log-distance from the reference that maps to the SMALLEST size " +
                 "(e.g. -2 ≈ about 7x smaller than typical).")]
        [SerializeField] private float minLogDistance = -2f;
        [Tooltip("Log-distance from the reference that maps to the LARGEST size " +
                 "(e.g. +2 ≈ about 7x bigger than typical).")]
        [SerializeField] private float maxLogDistance = 2f;
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

        [Header("Fonts")]
        [SerializeField] private TMP_FontAsset normalFont;
        [SerializeField] private TMP_FontAsset critFont; // headshot now; stochastic crit later

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

        public void Spawn(Vector3 worldPos, float amount, Color color, bool isCrit)
        {
            var n = available.Count > 0 ? available.Dequeue() : CreateOne();

            // Relative magnitude: where does this hit sit vs recent typical damage?
            // 0..1 normalized across the configured log-distance window.
            float logDist = DamageMagnitudeReference.Instance != null
                ? DamageMagnitudeReference.Instance.GetLogDistance(amount)
                : 0f;
            float magT = Mathf.InverseLerp(minLogDistance, maxLogDistance, logDist);

            // size, lifetime, and rise all derive from that relative magnitude
            float size = Mathf.Lerp(minSize, maxSize, magT);
            float life = Mathf.Lerp(minLifetime, maxLifetime, magT);
            float rise = baseRiseSpeed * (1f - riseSlowdown * magT);

            // feed the reference AFTER computing, so a hit sizes against the
            // range it arrived into, not one it just shifted
            if (DamageMagnitudeReference.Instance != null)
                DamageMagnitudeReference.Instance.Report(amount);

            n.Activate(
                pool: this,
                worldPos: worldPos,
                content: DamageNumberFormatter.Format(amount),
                color: color,
                lifetime: life,
                riseSpeed: rise,
                horizontalDrift: horizontalDrift,
                font: isCrit ? critFont : normalFont,
                fontSize: size);
        }
    }
}