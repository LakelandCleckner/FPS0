using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Stats;
using Combat.Info;

namespace Combat.Status
{
    public enum StatusDurationMode { PerEntryIndependent, RefreshAll, ExtendShared }
    public enum StatusEvictionStrategy { LowestWeight, ShortestRemaining }
    public enum StatusIntensityMode { Magnitude, Rate, Both }
    public enum StatusTickTimerMode { SharedPoolTimer, PerEntryTimer }
    public enum AuraEmitFrom { Prefab, Hulls, Skin }

    // Stateless status recipe. Phase 2i-b: the tick's derivation is authored as a
    // KIND + owner scope + stat/quantity, matching the data-driven DamageSpec.
    public abstract class StatusSO : ScriptableObject, IDescribable
    {
        [Header("Info")]
        [Tooltip("Keyword this status shows as in descriptions (Burn, Chill). Empty = the asset name, no popup.")]
        public KeywordSO keyword;

        [Header("VFX")]
        [Tooltip("Looping effect on the target while this status is active. Empty = none.")]
        public GameObject auraVfx;
        [Tooltip("Tint the aura to the damage type's colour.")]
        public bool tintAura = true;
        [Tooltip("Also tint white and grey parts of the aura. On for auras authored in white.")]
        public bool colorizeAura = false;
        [Min(0.01f)] public float auraScale = 1f;
        public Vector3 auraOffset = new Vector3(0f, 1f, 0f);
        [Tooltip("One-shot effect when the status ends or converts. Empty = none.")]
        public GameObject endVfx;
        [Min(0.05f)] public float endVfxLifetime = 1.5f;
        [Tooltip("Aura emission follows the stack count, full at the most stacks this status can show.")]
        public bool auraScalesWithStacks = false;
        [Tooltip("Stacks for full emission when there is no stack limit.")]
        [Min(1)] public int auraFullAtStacks = 10;
        [Tooltip("Prefab: the aura's own shape. Hulls: points on the hit hulls. Skin: baked skin points (falls back to Hulls).")]
        public AuraEmitFrom auraEmitFrom = AuraEmitFrom.Prefab;
        [Tooltip("Stack counts the enemy gallery shows for this status. Empty = up to 6 levels spread from 1 to the most it can show.")]
        public int[] previewStacks = new int[0];
        [Tooltip("Drawn over the body while active (FPS0/StatusOverlay). Up to 3 layers, strongest first.")]
        public Material overlayMaterial;
        [Tooltip("Overlay strength follows stacks / max stacks; off = always full.")]
        public bool overlayScalesWithStacks = true;
        [Tooltip("Overlay and aura stutter out over this many final seconds. 0 = cut off.")]
        [Min(0f)] public float fadeOutTime = 0f;

        [Header("Conversion")]
        [Tooltip("At this many stacks the status clears and applies Converts To instead (Chill into Freeze). 0 = never.")]
        [Min(0)] public int convertAtStacks = 0;
        [Tooltip("Applied when Convert At Stacks is reached. This status can't be applied while it's active.")]
        public StatusSO convertsTo;

        [Header("Control (when first applied)")]
        [Tooltip("Seconds the target can't move or attack (Freeze). 0 = none.")]
        [Min(0f)] public float stunTime = 0f;
        [Tooltip("Plays a stagger reaction and interrupts the target's attack.")]
        public bool stagger = false;
        [Tooltip("Heavy stagger instead of a normal one.")]
        public bool heavyStagger = false;
        [Tooltip("Metres the target is pushed away from the attacker. 0 = none.")]
        [Min(0f)] public float knockback = 0f;

        [Header("Stat Modifier (while active)")]
        [Tooltip("Stat this status modifies while active (e.g. Move Speed for a slow). " +
         "null = no modifier (damage-only statuses like burn).")]
        public StatDefinitionSO modifierTargetStat;

        [Tooltip("Bucket the modifier contributes to: 'mult' (multiplicative, e.g. -0.3 = " +
                 "30% slower), 'additive', or 'flat'.")]
        public string modifierBucket = "mult";

        [Tooltip("Modifier value PER UNIT OF INTENSITY. Final = this × CurrentIntensity, so " +
                 "the modifier deepens with stacks exactly as intensityMode dictates. E.g. " +
                 "-0.15 per intensity: 1 stack = -0.15 (15% slow), summed 3 stacks = -0.45.")]
        public float modifierPerIntensity = 0f;


        [Header("Tick Damage — derivation")]
        [Tooltip("Flat: fixed. PercentOfStat: % of a stat. PercentOfQuantity: % of a runtime quantity (health, ...).")]
        public DerivationKind derivationKind = DerivationKind.PercentOfStat;
        [Tooltip("Whose stat/quantity the tick reads.")]
        public StatScope owner = StatScope.Source;
        [Tooltip("PercentOfStat: which stat (e.g. weapon_damage on Source).")]
        public StatDefinitionSO stat;
        [Tooltip("PercentOfQuantity: which runtime quantity.")]
        public QuantityKind quantity = QuantityKind.CurrentHealth;
        [Tooltip("Multiplier applied to whatever the derivation points at (0.2 = 20%).")]
        public float coefficient = 0.2f;
        public DamageTypeSO damageType;

        [Header("Timing")]
        public float duration = 3f;
        public float tickInterval = 0.5f;

        [Header("Stack Cap")]
        [Tooltip("Max entries in the pool. 0 = uncapped.")]
        public int maxEntries = 0;
        public StatusEvictionStrategy evictionStrategy = StatusEvictionStrategy.LowestWeight;

        [Header("Duration Behaviour")]
        public StatusDurationMode durationMode = StatusDurationMode.PerEntryIndependent;

        [Header("ExtendShared settings (only used in ExtendShared mode)")]
        public bool extendByOriginalDuration = true;
        public float extendAmount = 1f;
        public float extensionCap = 0f;

        [Header("Intensity (how stacks scale the DOT)")]
        public StatusIntensityMode intensityMode = StatusIntensityMode.Magnitude;
        public float intervalReductionPerStack = 0f;
        public float minInterval = 0.1f;

        [Header("Tick Timer")]
        public StatusTickTimerMode tickTimerMode = StatusTickTimerMode.SharedPoolTimer;

        [Header("Damage Number Presentation")]
        public bool showFloatingNumber = true;
        public bool feedsAccumulator = true;

        [Header("On-Kill Reactions")]
        [Tooltip("Effects that run when a TICK of this status kills the target. Drop an " +
         "Explosion On Kill here and burn-kills detonate. Independent of any " +
         "explosion the weapon itself carries, so they're balanced separately. " +
         "These run in the tick's Reaction phase — they check WasKill themselves.")]
        public System.Collections.Generic.List<Combat.Effects.HitEffectSO> onKillEffects
            = new System.Collections.Generic.List<Combat.Effects.HitEffectSO>();

        [Header("Transfer")]
        [Tooltip("When this status transfers to a new target (e.g. via an explosion), " +
         "reset its tick cadence. OFF (default) preserves the tick timer so a SLOW " +
         "DOT doesn't sit for a full interval before its first tick on the new " +
         "target — avoids a dead beat. ON restarts the cadence cleanly, better for " +
         "fast DOTs where a near-immediate carried-over tick would feel off.")]
        public bool transferResetsTickCadence = false;



        // Most stacks the aura shows: the stack cap (or the custom count), one below conversion.
        public int AuraMaxStacks
        {
            get
            {
                int m = maxEntries > 0 ? maxEntries : auraFullAtStacks;
                if (convertsTo != null && convertAtStacks > 0) m = Mathf.Min(m, convertAtStacks - 1);
                return Mathf.Max(1, m);
            }
        }

        // Visual strength in the fade-out window: full, then flickering off more often as time runs out.
        public float FadeAt(float remaining, int seed)
        {
            if (fadeOutTime <= 0f || remaining >= fadeOutTime) return 1f;
            float r = Mathf.Clamp01(remaining / fadeOutTime);
            uint h = (uint)(Mathf.FloorToInt(Time.time * 20f) * 73856093) ^ (uint)(seed * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 1023) / 1023f < r ? Mathf.Lerp(0.35f, 1f, r) : 0f;
        }

        // Stack levels for previews: the custom list, else every level up to 6, else 6 spread evenly.
        public List<int> PreviewLevels()
        {
            int max = AuraMaxStacks;
            var levels = new List<int>();
            if (previewStacks != null && previewStacks.Length > 0)
            {
                foreach (int n in previewStacks)
                {
                    int c = Mathf.Clamp(n, 1, max);
                    if (!levels.Contains(c)) levels.Add(c);
                }
                levels.Sort();
                return levels;
            }
            const int Most = 6;
            if (max <= Most) { for (int i = 1; i <= max; i++) levels.Add(i); return levels; }
            for (int i = 0; i < Most; i++)
            {
                int c = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, max, i / (float)(Most - 1))));
                if (!levels.Contains(c)) levels.Add(c);
            }
            return levels;
        }

        // Numbers for this status: damage per tick and in total, stat change, duration and stacking.
        public virtual void Describe(Describer d)
        {
            bool deals = coefficient > 0f && (derivationKind != DerivationKind.PercentOfStat || stat != null);
            if (deals)
            {
                int ticks = tickInterval > 0f ? Mathf.Max(1, Mathf.FloorToInt(duration / tickInterval + 0.001f)) : 1;
                d.Add("Deals ").Damage(derivationKind, stat, quantity, owner, coefficient, "tick");
                if (damageType != null) d.Add(" as ").Element(damageType);
                d.Add(" every ").Seconds(tickInterval, "interval").Add(" for ").Seconds(duration, "duration")
                 .Add(" (").Damage(derivationKind, stat, quantity, owner, coefficient * ticks, "total").Add(" total).");
            }
            if (modifierTargetStat != null && Mathf.Abs(modifierPerIntensity) > 0f)
            {
                d.Next();
                bool mult = modifierBucket == "mult";
                string amount = mult ? Describer.Pct(Mathf.Abs(modifierPerIntensity)) : Describer.StatValue(modifierTargetStat, Mathf.Abs(modifierPerIntensity));
                d.Add(modifierPerIntensity < 0f ? "Lowers " : "Raises ").Add(Describer.StatName(modifierTargetStat)).Add(" by ").Num(amount, "amount");
                d.Add(" for ").Seconds(duration, "duration").Add(maxEntries > 1 ? " per stack." : ".");
            }
            if (maxEntries > 1) d.Next().Add("Stacks up to ").Count(maxEntries, "stacks").Add(" times.");
            if (convertsTo != null && convertAtStacks > 0)
                d.Next().Add("At ").Count(convertAtStacks, "convert").Add(" stacks it becomes ").StatusLink(convertsTo).Add(".");
            else if (maxEntries == 0 && deals) d.Next().Add("Stacks without limit.");
            if (d.IsEmpty) d.Add(name);
        }

        // Build the tick's DamageSpec from the authored derivation.
        public DamageSpec BuildTickSpec()
        {
            switch (derivationKind)
            {
                case DerivationKind.Flat:
                    return new DamageSpec(coefficient, damageType);
                case DerivationKind.PercentOfQuantity:
                    return new DamageSpec(quantity, owner, coefficient, damageType);
                case DerivationKind.PercentOfStat:
                default:
                    return new DamageSpec(stat, owner, coefficient, damageType);
            }
        }
    }
}