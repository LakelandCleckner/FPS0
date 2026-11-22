using UnityEngine;
using TMPro;

namespace Combat.Core
{
    // Particle trail a shot of this type sheds in flight.
    public enum TrailStyle { None, Embers, Mist, Sparks, Drips }

    // A damage type as a ScriptableObject asset. Replaces the old DamageType
    // enum. Owns presentation data (font, color gradients, sound) and is the
    // identity used for resistance lookups and future type interactions.
    //
    // Damage-number styling: each type owns a font and FOUR gradient states
    // (normal / crit / debuffed / crit+debuffed). The crit and debuff axes are
    // independent and combine, applied as a 2x2 alongside TMP style flags
    // (bold for crit, italic for debuff, bold+italic for both). Gradients are
    // authored inline per type (VertexGradient = four corner colors; set all
    // four equal for a flat color, or top/bottom pairs for a vertical two-tone).
    [CreateAssetMenu(fileName = "DamageType", menuName = "Combat/Damage Type")]
    public class DamageTypeSO : ScriptableObject
    {
        [Tooltip("Stable lookup id. Set once and DO NOT change — saves/lookups key on this.")]
        public string id = "physical";

        [Tooltip("Display name shown in UI. Safe to rename.")]
        public string displayName = "Physical";

        [Tooltip("World colour for this type's pools, blasts and other effects.")]
        public Color color = Color.white;

        [Tooltip("UI icon: white fill and dark outline, tinted to the colour above.")]
        public UnityEngine.Sprite icon;
        [Tooltip("Sprite name in the inline text sprite asset. Empty = no inline icon.")]
        public string inlineSprite = "";

        private static readonly System.Collections.Generic.List<DamageTypeSO> loaded = new System.Collections.Generic.List<DamageTypeSO>();

        private void OnEnable() { if (!loaded.Contains(this)) loaded.Add(this); }
        private void OnDisable() => loaded.Remove(this);

        // The loaded damage type that owns this keyword, or null.
        public static DamageTypeSO ForKeyword(Object keyword)
        {
            if (keyword == null) return null;
            for (int i = 0; i < loaded.Count; i++)
                if (loaded[i] != null && loaded[i].keyword == keyword) return loaded[i];
            return null;
        }

        // Rich-text inline icon tinted to this type, with a trailing space. Empty without a sprite name.
        public string InlineIcon()
            => string.IsNullOrEmpty(inlineSprite) ? ""
             : "<color=#" + ColorUtility.ToHtmlStringRGB(color) + "><sprite name=\"" + inlineSprite + "\" tint=1></color> ";

        [Tooltip("Optional hit/tick sound for this type. May be left empty for now.")]
        public AudioClip hitSound;

        [Header("Damage Number Font")]
        [Tooltip("Font family for this type's damage numbers. Style (bold/italic) " +
                 "is applied on top via flags, so one font covers all states.")]
        public TMP_FontAsset font;

        [Header("Damage Number Gradients (2x2: crit x debuff)")]
        [Tooltip("Base look — normal hit, no crit, no debuff.")]
        public VertexGradient normalGradient = Flat(Color.white);
        [Tooltip("Crit (currently driven by headshots).")]
        public VertexGradient critGradient = Flat(Color.white);
        [Tooltip("Debuffed hit (trigger defined later; settable for testing now).")]
        public VertexGradient debuffedGradient = Flat(Color.white);
        [Tooltip("Crit AND debuffed.")]
        public VertexGradient critDebuffedGradient = Flat(Color.white);

        [Header("Defense")]
        [Tooltip("This type's resistance stat on a target (e.g. fire_resistance). " +
         "Resolved from the target's container; 0 = no resist, 0.5 = takes half, " +
         "negative (from a debuff) = takes more. Leave null for no type resistance.")]
        public Combat.Stats.StatDefinitionSO resistanceStat;

        [Header("Info")]
        [Tooltip("Keyword this element shows as in descriptions. Empty = plain name.")]
        public Combat.Info.KeywordSO keyword;

        [Header("Status")]
        [Tooltip("Status this element applies by default, e.g. from enemy blasts and pools that take the enemy's element.")]
        public Combat.Status.StatusSO status;

        [Header("Direct Hits")]
        [Tooltip("Multiplies direct-hit damage from weapons of this type. Ballistic's edge over elements.")]
        public float directDamageMultiplier = 1f;
        [Tooltip("Added to the weapon's precision multiplier on precision hits.")]
        public float precisionBonus = 0f;

        [Header("VFX")]
        [Tooltip("Also tint white and grey parts of this type's effects. On for effects authored in white.")]
        public bool colorizeVfx = false;
        [Tooltip("Spawned at direct-hit points, tinted to this type's colour. Empty = none.")]
        public GameObject hitVfx;
        [Tooltip("Seconds between hit effects of this type, so fast guns don't spawn one per bullet.")]
        [Min(0f)] public float hitVfxInterval = 0.08f;
        [Min(0.05f)] public float hitVfxLifetime = 1f;
        [Min(0.01f)] public float hitVfxScale = 0.5f;
        [Tooltip("Metres the hit effect is pushed out from the surface so it doesn't clip into it.")]
        [Min(0f)] public float hitVfxOffset = 0.12f;
        [Tooltip("Spawned for spreads of this type (kill spreads, area applies).")]
        public GameObject blastVfx;
        [Tooltip("Radius the blast prefab was built at; spawned scale is radius / this. 0 = authored scale.")]
        [Min(0f)] public float blastReferenceRadius = 3f;
        [Min(0.05f)] public float blastLifetime = 2f;
        [Tooltip("Line material for chain arcs of this type. Empty = a plain unlit line.")]
        public Material arcMaterial;

        [Header("Tracer")]
        [Tooltip("Hitscan shots of this type draw a tracer from the muzzle; the hit effect then plays when it lands.")]
        public bool tracer = true;
        [Tooltip("Draw the coloured streak line.")]
        public bool tracerStreak = true;
        [Tooltip("Projectile body flown along the tracer, tinted to this type. Empty = streak only.")]
        public GameObject tracerVfx;
        [Min(0.01f)] public float tracerScale = 0.3f;
        [Tooltip("Visual speed in metres per second. Damage is already instant.")]
        [Min(1f)] public float tracerSpeed = 300f;
        [Tooltip("Streak length in metres.")]
        [Min(0.1f)] public float tracerLength = 3f;
        [Tooltip("Streak width multiplier.")]
        [Min(0.1f)] public float tracerWidth = 1f;
        [Tooltip("Seconds the tracer stays after landing so trails can fade.")]
        [Min(0f)] public float tracerLinger = 0.12f;
        [Tooltip("Particle trail shed along the shot in flight.")]
        public TrailStyle trailStyle = TrailStyle.None;
        [Tooltip("Particles per metre, times the style's base rate.")]
        [Min(0f)] public float trailDensity = 1f;
        [Tooltip("Particle size multiplier.")]
        [Min(0.01f)] public float trailSize = 1f;


        // Picks the gradient for the current modifier combination. Falls back to
        // the normal gradient if a slot was left at default (incremental authoring).
        public VertexGradient GetGradient(bool isCrit, bool isDebuffed)
        {
            if (isCrit && isDebuffed) return critDebuffedGradient;
            if (isCrit) return critGradient;
            if (isDebuffed) return debuffedGradient;
            return normalGradient;
        }

        // Style flags for the modifier combination: bold = crit, italic = debuff.
        public FontStyles GetStyle(bool isCrit, bool isDebuffed)
        {
            FontStyles s = FontStyles.Normal;
            if (isCrit) s |= FontStyles.Bold;
            if (isDebuffed) s |= FontStyles.Italic;
            return s;
        }

        // Convenience: a flat (single-color) gradient.
        private static VertexGradient Flat(Color c) => new VertexGradient(c, c, c, c);
    }
}