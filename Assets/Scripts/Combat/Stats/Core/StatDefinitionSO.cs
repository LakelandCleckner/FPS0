using UnityEngine;

namespace Combat.Stats
{
    // How a stat's base relates to its buckets at resolution.
    public enum StatResolutionMode
    {
        // (base + flat) × (1 + Σ additive) × Π(mult).  base is a real quantity the
        // buckets SCALE (weapon_damage, rpm, reload, magazine).
        ScaledBase,

        // (base + flat + Σ additive) × Π(mult).  the value IS a pool total; base is
        // the INHERENT contribution living IN the additive pool (crit_damage's +50%,
        // crit_chance, global_damage, damage_taken). Matches the D4 crit model.
        Pool
    }

    // A single stat's IDENTITY + metadata, as an authorable asset (like DamageTypeSO).
    [CreateAssetMenu(fileName = "StatDefinition", menuName = "Combat/Stats/Stat Definition")]
    public class StatDefinitionSO : ScriptableObject
    {
        [Tooltip("Stable id. Also sorts stats into deterministic runtime indices — " +
                 "don't casually rename once assets reference this.")]
        public string id = "";
        public string displayName = "";

        [Header("Resolution")]
        [Tooltip("ScaledBase: (base+flat)×(1+additive)×mult — base is scaled by buckets.\n" +
                 "Pool: (base+flat+additive)×mult — base is the inherent IN the additive pool.")]
        public StatResolutionMode resolutionMode = StatResolutionMode.ScaledBase;

        [Tooltip("Value returned when an entity has no base set. For Pool stats this " +
                 "is the inherent (e.g. crit_damage 0.5 = inherent +50%).")]
        public float defaultValue = 0f;

        [Tooltip("Clamp the final resolved value to a minimum (after all buckets).")]
        public bool useMin = false;
        public float minValue = 0f;
        [Tooltip("Clamp the final resolved value to a maximum (after all buckets).")]
        public bool useMax = false;
        public float maxValue = 0f;

        [Header("Display hints (for future UI)")]
        public bool isPercent = false;
        public int displayDecimals = 0;

        [System.NonSerialized] public int RuntimeIndex = -1;

        public float Clamp(float value)
        {
            if (useMin && value < minValue) value = minValue;
            if (useMax && value > maxValue) value = maxValue;
            return value;
        }
    }
}