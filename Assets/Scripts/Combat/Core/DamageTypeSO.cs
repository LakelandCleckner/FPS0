using UnityEngine;
using TMPro;

namespace Combat.Core
{
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

        [Header("Direct Hits")]
        [Tooltip("Multiplies direct-hit damage from weapons of this type. Ballistic's edge over elements.")]
        public float directDamageMultiplier = 1f;
        [Tooltip("Added to the weapon's precision multiplier on precision hits.")]
        public float precisionBonus = 0f;

        [Header("VFX")]
        [Tooltip("Spawned at direct-hit points, tinted to this type's colour. Empty = none.")]
        public GameObject hitVfx;
        [Tooltip("Seconds between hit effects of this type, so fast guns don't spawn one per bullet.")]
        [Min(0f)] public float hitVfxInterval = 0.08f;
        [Min(0.05f)] public float hitVfxLifetime = 1f;
        [Min(0.01f)] public float hitVfxScale = 0.5f;
        [Tooltip("Spawned for spreads of this type (kill spreads, area applies).")]
        public GameObject blastVfx;
        [Tooltip("Radius the blast prefab was built at; spawned scale is radius / this. 0 = authored scale.")]
        [Min(0f)] public float blastReferenceRadius = 3f;
        [Min(0.05f)] public float blastLifetime = 2f;
        [Tooltip("Line material for chain arcs of this type. Empty = a plain unlit line.")]
        public Material arcMaterial;


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