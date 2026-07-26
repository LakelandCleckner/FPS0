#if UNITY_EDITOR || STATUS_DEBUG
using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;

namespace Combat.Testing
{
    // THROWAWAY verification for the chain/AoE primitive (piece 1). NOT the real
    // explode-on-kill — that's an authored IHitEffect in piece 2. This exists only to
    // prove the queue drains in the right order and bounds itself, then gets deleted.
    //
    // It's a Reaction-phase effect: add it to a weapon's effect list temporarily (or
    // drop it in via a debug hook). On a kill it finds neighbours and enqueues a
    // sibling AoE hit against each, carrying a flat damage effect.
    //
    // WHAT TO WATCH IN THE CONSOLE / GAME:
    //  1. ORDER — the victim's own damage number and Kill event appear BEFORE any
    //     neighbour's. If a neighbour's number beats the victim's, the drain is firing
    //     synchronously instead of deferred.
    //  2. DEPTH CAP — set the weapon's MaxChainDepth to 1. If a neighbour dies from the
    //     splash, its death must NOT spawn a third hit (siblings don't chain, but this
    //     also confirms the depth guard on the link path when you switch to links).
    //  3. NO CORPSE RE-HIT — two enemies adjacent, both killed; neither blast re-hits
    //     the other's corpse (one damage number per neighbour, not two).
    public class TestExplodeOnKill : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Reaction;   // runs AFTER damage/kill
        public bool PropagatesOnChain => false;             // the test doesn't re-arm itself

        private readonly float radius;
        private readonly int layerMask;
        private readonly DamageSpec splashSpec;

        // Reused across cascades so the test itself doesn't allocate per kill.
        private readonly List<ICombatant> found = new List<ICombatant>(16);
        private readonly List<IHitEffect> splashEffects;
        private NeighbourFinder finder;

        public TestExplodeOnKill(float radius, int layerMask, DamageSpec splashSpec)
        {
            this.radius = radius;
            this.layerMask = layerMask;
            this.splashSpec = splashSpec;
            splashEffects = new List<IHitEffect> { new DamageHitEffect(splashSpec) };
        }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            if (!ctx.WasKill) return;

            // The resolver is the concrete WeaponHitResolver; the primitive lives there.
            var whr = resolver as WeaponHitResolver;
            if (whr == null) return;

            finder ??= new NeighbourFinder(layerMask);

            // The cascade's shared dedup set. Seed it with the victim so the blast
            // doesn't try to hit the corpse that triggered it — this is the on-kill
            // case, where originator-inclusion is correct.
            var alreadyHit = new HashSet<ICombatant> { ctx.Target };

            Vector3 center = (ctx.Target as MonoBehaviour) != null
                ? ((MonoBehaviour)ctx.Target).transform.position
                : ctx.HitPoint;

            finder.FindInRadius(center, radius, ctx.SourceFaction, alreadyHit, found);
            if (found.Count == 0) return;

            // Sibling hits: one delivery (the explosion) striking all neighbours at the
            // parent's depth, full damage. Deferred — drains after this resolution's
            // feedback and events, so the victim's number lands first.
            whr.EnqueueSiblingHits(ctx, found, splashEffects, alreadyHit);
        }
    }
}
#endif
