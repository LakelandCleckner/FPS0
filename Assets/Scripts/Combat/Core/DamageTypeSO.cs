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