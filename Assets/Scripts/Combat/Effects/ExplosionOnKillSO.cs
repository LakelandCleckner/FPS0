using UnityEngine;
using Combat.Core;
using Combat.Stats;
using Combat.Status;

namespace Combat.Effects
{
    // Authored on-kill explosion. Drop onto a weapon's riderEffects like any other
    // rider (burn, etc.) — the whole point is that it rides the EXISTING effect
    // pipeline, no perk system required. When riders eventually migrate to perks, this
    // migrates with them; nothing here assumes it stays a rider.
    //
    // TWO BEHAVIOURS FROM ONE ASSET:
    //   status empty  -> explosion deals damage only
    //   status filled -> explosion deals damage AND applies that status to each target
    // Same effect type; the authored fields decide. No separate class per variant.
    //
    // The damage defaults to a percent of the wielder's weapon_damage, so the
    // explosion scales with the gun (and with any weapon-damage buff) automatically —
    // the same derivation base damage uses. Author a flat value or a different stat if
    // a specific weapon wants it.
    [CreateAssetMenu(fileName = "ExplosionOnKill", menuName = "Combat/Effects/Explosion On Kill")]
    public class ExplosionOnKillSO : HitEffectSO
    {
        [Header("Blast")]
        [Tooltip("Radius of the detonation around the victim.")]
        public float radius = 4f;

        [Tooltip("Layer(s) the blast can hit. Set to your enemy layer.")]
        public LayerMask hitLayers;

        [Header("Damage")]
        [Tooltip("Stat the blast damage derives from. weapon_damage makes the explosion " +
                 "scale with the gun, like base damage.")]
        public StatDefinitionSO damageStat;

        [Tooltip("Fraction of the stat dealt to each target (0.5 = 50% of weapon_damage).")]
        public float damageCoefficient = 0.5f;

        [Tooltip("Damage type of the blast. Defaults sensibly if left null at author time " +
                 "is not possible — assign one.")]
        public DamageTypeSO damageType;

        [Header("Status (optional)")]
        [Tooltip("If set, the explosion also applies this status to every target it hits " +
                 "— the 'explosion that spreads a DOT' version. Leave empty for a plain " +
                 "damage explosion.")]
        public StatusSO applyStatus;

        [Header("Chaining")]
        [Tooltip("-1 = inherit the weapon's Max Chain Depth. Set >= 0 to give this " +
                 "explosion its own cap, independent of the gun. Only matters if the " +
                 "blast's own hits go on to chain.")]
        public int maxChainDepthOverride = -1;

        [Header("VFX (placeholder — no pool yet)")]
        [Tooltip("Spawned once at the blast origin. Instantiated and destroyed after " +
                 "the lifetime below; swap for a pooled spawn when VFX pooling exists.")]
        public GameObject blastVfx;

        [Tooltip("Seconds before the spawned VFX is destroyed. 0 = never (don't do that " +
                 "without a pool).")]
        public float vfxLifetime = 2f;

        [Tooltip("The radius the VFX prefab was BUILT at (its natural size). The spawned " +
                 "effect scales by radius / this, so one prefab fits any blast size. " +
                 "0 = don't scale, use the prefab's authored scale as-is.")]
        public float vfxReferenceRadius = 0f;

        [Header("Placeholder VFX (when no prefab is assigned)")]
        [Tooltip("With no blastVfx prefab, spawn a translucent sphere at the blast " +
                 "radius that expands and fades — a stand-in until real VFX exists, and " +
                 "a live check that the hit area matches.")]
        public bool usePlaceholderVfx = true;

        [Tooltip("Colour of the placeholder sphere. Alpha controls starting opacity.")]
        public Color placeholderColor = new Color(1f, 0.4f, 0.1f, 0.35f);

        // Holds a NeighbourFinder buffer, so it's genuinely stateful — never share one
        // instance across weapons, or two simultaneous detonations fight over the
        // overlap buffer. HitEffectSO rebuilds each GetInstance() when this is true.
        protected override bool RequiresFreshInstance => true;

        protected override IHitEffect Build()
        {
            var spec = new DamageSpec(
                damageStat, StatScope.Source, damageCoefficient, damageType);

            return new ExplosionOnKillEffect(
                radius: radius,
                layerMask: hitLayers.value,
                blastSpec: spec,
                statusToApply: applyStatus,
                maxChainDepthOverride: maxChainDepthOverride,
                blastVfx: blastVfx,
                vfxLifetime: vfxLifetime,
                vfxReferenceRadius: vfxReferenceRadius,
                usePlaceholderVfx: usePlaceholderVfx,
                placeholderColor: placeholderColor);
        }
    }
}