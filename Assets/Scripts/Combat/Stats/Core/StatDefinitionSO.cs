using UnityEngine;

namespace Combat.Stats
{
    // A single stat's IDENTITY + metadata, as an authorable asset (like DamageTypeSO).
    // Other systems reference the stat by dragging this asset (type-safe, no typos);
    // at runtime it resolves to a fast integer index (see StatRegistry) so storage
    // and lookup are array-fast.
    //
    // Phase 2a: identity + metadata only. Buckets, modifiers, and resolution come
    // in later phases; the metadata here (default, clamps) is already what those
    // phases will read.
    [CreateAssetMenu(fileName = "StatDefinition", menuName = "Combat/Stats/Stat Definition")]
    public class StatDefinitionSO : ScriptableObject
    {
        [Tooltip("Stable id. Also used to sort stats into deterministic runtime " +
                 "indices, so DON'T casually rename it once assets reference this.")]
        public string id = "";

        [Tooltip("Display name (safe to rename).")]
        public string displayName = "";

        [Header("Resolution")]
        [Tooltip("Value returned when an entity has no base set for this stat.")]
        public float defaultValue = 0f;

        [Tooltip("Clamp the final resolved value to a minimum (after all buckets).")]
        public bool useMin = false;
        public float minValue = 0f;

        [Tooltip("Clamp the final resolved value to a maximum (after all buckets).")]
        public bool useMax = false;
        public float maxValue = 0f;

        [Header("Display hints (for future UI)")]
        [Tooltip("Show as a percentage in UI (0.15 -> 15%).")]
        public bool isPercent = false;
        [Tooltip("Decimal places for display.")]
        public int displayDecimals = 0;

        // Runtime index assigned by StatRegistry at startup. NOT serialized — it's
        // derived fresh each run so it can't drift. -1 until the registry assigns it.
        [System.NonSerialized] public int RuntimeIndex = -1;

        // Apply this stat's optional min/max clamp to a resolved value.
        public float Clamp(float value)
        {
            if (useMin && value < minValue) value = minValue;
            if (useMax && value > maxValue) value = maxValue;
            return value;
        }
    }
}
