using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Status;

namespace Combat.Effects
{
    // Runtime effect: when a hit KILLS, detonate an area around the victim, dealing an
    // authored damage spec to every enemy in radius and optionally applying a status
    // to each. Produced by ExplosionOnKillSO.
    //
    // It is a REACTION-phase effect, so it runs after the Application-phase damage that
    // set WasKill — it reads the kill the same resolution produced.
    //
    // The blast is SIBLINGS, not chain links: one detonation striking everyone in
    // radius at the parent's depth, full damage. (A version that chains explosions off
    // each kill would use EnqueueChainLinks and is a later effect.) Deferred through
    // the resolver, so the victim's own damage number and Kill event land BEFORE the
    // blast's — the reason the primitive is a queue and not a synchronous cascade.
    //
    // STATELESS BY CONTRACT except the NeighbourFinder's scratch buffer, so
    // ExplosionOnKillSO returns a fresh instance (RequiresFreshInstance) rather than a
    // shared singleton — two weapons detonating in the same frame must not share the
    // overlap buffer. All per-hit data still lives on the context; nothing here holds
    // target state between detonations.
    public class ExplosionOnKillEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Reaction;

        // This effect does not itself carry forward to a chain link — an explosion
        // shouldn't recursively re-arm on its OWN splash hits via the effect list. If
        // the splash kills and should re-detonate, that's the depth-capped chain path,
        // authored deliberately, not this flag.
        public bool PropagatesOnChain => false;

        private readonly float radius;
        private readonly int layerMask;
        private readonly DamageSpec blastSpec;
        private readonly StatusSO statusToApply;   // null = damage-only
        private readonly int maxChainDepthOverride; // < 0 = inherit from the weapon/context
        private readonly GameObject blastVfx;
        private readonly float vfxLifetime;
        private readonly float vfxReferenceRadius;   // radius the prefab was authored at
        private readonly bool usePlaceholderVfx;
        private readonly Color placeholderColor;

        private NeighbourFinder finder;
        private readonly List<ICombatant> found = new List<ICombatant>(16);

        // The blast's effect list. Built ONCE — the effects are stateless, so the same
        // DamageHitEffect (and optional ApplyStatusHitEffect) serve every detonation.
        private readonly List<IHitEffect> blastEffects;

        public ExplosionOnKillEffect(
            float radius,
            int layerMask,
            DamageSpec blastSpec,
            StatusSO statusToApply,
            int maxChainDepthOverride,
            GameObject blastVfx,
            float vfxLifetime,
            float vfxReferenceRadius,
            bool usePlaceholderVfx,
            Color placeholderColor)
        {
            this.radius = radius;
            this.layerMask = layerMask;
            this.blastSpec = blastSpec;
            this.statusToApply = statusToApply;
            this.maxChainDepthOverride = maxChainDepthOverride;
            this.blastVfx = blastVfx;
            this.vfxLifetime = vfxLifetime;
            this.vfxReferenceRadius = vfxReferenceRadius;
            this.usePlaceholderVfx = usePlaceholderVfx;
            this.placeholderColor = placeholderColor;

            blastEffects = new List<IHitEffect>(2) { new DamageHitEffect(blastSpec) };
            if (statusToApply != null)
                blastEffects.Add(new ApplyStatusHitEffect(statusToApply));
        }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            if (!ctx.WasKill) return;

            var whr = resolver as WeaponHitResolver;
            if (whr == null) return;

            finder ??= new NeighbourFinder(layerMask);

            Vector3 center = (ctx.Target as MonoBehaviour) != null
                ? ((MonoBehaviour)ctx.Target).transform.position
                : ctx.HitPoint;

            // VFX: one blast at the origin. Placeholder Instantiate/Destroy — replace
            // with a pool when one exists. Spawned even if no targets are found, so the
            // explosion still reads visually against a lone enemy.
            if (blastVfx != null)
            {
                var fx = Object.Instantiate(blastVfx, center, Quaternion.identity);

                // Scale the effect to the blast radius, so one authored prefab fits
                // any radius. referenceRadius is the radius the prefab was BUILT at
                // (its natural size); a blast twice that radius spawns it at 2x scale.
                // If no reference is set, leave the prefab at its authored scale.
                if (vfxReferenceRadius > 0f)
                {
                    float k = radius / vfxReferenceRadius;
                    fx.transform.localScale = blastVfx.transform.localScale * k;
                }

                if (vfxLifetime > 0f) Object.Destroy(fx, vfxLifetime);
            }
            else if (usePlaceholderVfx)
            {
                // No authored VFX yet: a translucent sphere at the real blast radius
                // that expands and fades. Sized to the radius, so it doubles as a
                // runtime check that the hit area matches what you expect.
                BlastPlaceholderVfx.Spawn(center, radius,
                    vfxLifetime > 0f ? vfxLifetime : 0.4f, placeholderColor);
            }

            // The cascade's shared dedup set, SEEDED WITH THE VICTIM — this is the
            // on-kill case, so the blast must not try to hit the corpse that triggered
            // it. (An on-hit arc across living enemies would seed empty; this doesn't.)
            var alreadyHit = new HashSet<ICombatant> { ctx.Target };

            finder.FindInRadius(center, radius, ctx.SourceFaction, alreadyHit, found);
            if (found.Count == 0) return;

            // Optionally override the depth cap for this specific explosion. The parent
            // context carries the weapon's MaxChainDepth; a per-effect override lets an
            // explosion cap its own cascade regardless of the gun. Siblings don't
            // increment depth, so this only matters if the blast's own effects chain.
            if (maxChainDepthOverride >= 0)
                ctx.MaxChainDepth = maxChainDepthOverride;

            // SIBLINGS: full damage to everyone in radius, at the parent's depth.
            whr.EnqueueSiblingHits(ctx, found, blastEffects, alreadyHit);
        }
    }
}