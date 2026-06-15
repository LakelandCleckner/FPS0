using UnityEngine;

namespace Combat.Feedback
{
    // Tracks a single decaying reference for "typical recent damage magnitude,"
    // in LOG space so it stays sane across single digits to billions. Every
    // damage event nudges the reference toward that hit's log-magnitude; over
    // time with no input the reference holds, and as the player's damage range
    // shifts (build changes, scaling up) the reference drifts to follow.
    //
    // A spawning number sizes itself by its LOG-DISTANCE from this reference:
    // at/above reference -> large, well below -> small. This reproduces the
    // Diablo-style look where the biggest recent hits pop and chip damage
    // recedes, recalibrating automatically as power grows.
    //
    // Singleton-ish so the pool can consult it without wiring a reference.
    public class DamageMagnitudeReference : MonoBehaviour
    {
        public static DamageMagnitudeReference Instance { get; private set; }

        [Header("Reference Smoothing")]
        [Tooltip("How fast the reference chases recent damage. Higher = snappier, " +
                 "lower = smoother/slower to recalibrate. This is the 'respec " +
                 "recalibration' speed.")]
        [SerializeField] private float adaptSpeed = 2f;

        [Tooltip("Seed value before any damage is seen, so early hits have a sane reference.")]
        [SerializeField] private float initialReferenceDamage = 20f;

        // stored in log space
        private float logReference;
        private bool seeded;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            logReference = Mathf.Log(Mathf.Max(1f, initialReferenceDamage));
            seeded = true;
        }

        // Feed every damage event here. Nudges the reference toward this hit.
        public void Report(float damage)
        {
            if (damage <= 0f) return;

            float logD = Mathf.Log(Mathf.Max(1f, damage));

            if (!seeded)
            {
                logReference = logD;
                seeded = true;
                return;
            }

            // exponential smoothing toward the latest hit, frame-rate independent
            float t = 1f - Mathf.Exp(-adaptSpeed * Time.deltaTime);
            logReference = Mathf.Lerp(logReference, logD, t);
        }

        // Returns how this damage compares to the current reference, as a signed
        // log-distance. 0 = at reference, positive = bigger than typical,
        // negative = smaller. The pool maps this to a size.
        public float GetLogDistance(float damage)
        {
            float logD = Mathf.Log(Mathf.Max(1f, damage));
            return logD - logReference;
        }
    }
}
