using System.Collections.Generic;

namespace Combat.Core
{
    // The DEFERRED MULTI-TARGET RESOLUTION primitive. Anything that turns one hit or
    // one delivery into several — an AoE rocket, an explosion, a chain arc, and later
    // perk-caused hits — enqueues here, and the resolver drains AFTER the current
    // top-level resolution finishes its feedback and events.
    //
    // WHY DEFERRED: WeaponHitResolver runs effects, THEN feedback, THEN events. If a
    // blast or reaction re-entered ResolveHit inline, a splash target's damage number
    // and Kill event would fire before the primary hit's — the corpse's own number
    // arriving after the explosion it caused. Deferring makes the primary hit resolve
    // completely, then the extra hits drain in order. Same reason StatusManager defers.
    //
    // TWO ENTRY POINTS, because AoE and chains differ:
    //   EnqueueSiblings — all targets of ONE delivery, at the parent's depth, full
    //                     damage. A rocket: everyone in radius takes a direct hit.
    //   EnqueueLinks    — successive hops, depth incrementing, falloff compounding.
    //                     Lightning arc, explosion cascade.
    // Both share the queue, pool, drain and dedup. They differ only in how each child
    // context is seeded (see ChainContextPool).
    public class ChainResolver
    {
        private readonly IHitResolver resolver;
        private readonly ChainContextPool pool = new ChainContextPool();
        private readonly Queue<HitContext> queue = new Queue<HitContext>(32);
        private bool draining;

        public ChainResolver(IHitResolver resolver)
        {
            this.resolver = resolver;
        }

        // ONE delivery striking many targets. Depth stays at the parent's, damage is
        // full for every target. The blast's originating victim (for an explosion) or
        // the pierced target (for a rocket that also directly hit someone) should be
        // pre-seeded into `sharedAlreadyHit` by the caller so the splash doesn't
        // double-hit them.
        //
        // Depth is NOT checked here the way it is for links — a sibling is at the
        // parent's depth, so an AoE at depth 0 always resolves. But the shared set
        // still dedups, so one blast can't hit a target twice.
        public void EnqueueSiblings(
            HitContext parent,
            IReadOnlyList<ICombatant> targets,
            List<IHitEffect> effects,
            HashSet<ICombatant> sharedAlreadyHit)
        {
            if (targets == null || effects == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || t.IsDying) continue;
                if (sharedAlreadyHit.Contains(t)) continue;
                sharedAlreadyHit.Add(t);

                queue.Enqueue(pool.RentSibling(parent, t, effects, sharedAlreadyHit));
            }
        }

        // Successive chain HOPS. Depth increments and is capped by MaxChainDepth — the
        // failsafe for an unbounded fresh-target cascade. The shared set is the
        // ordinary terminator (no target hit twice); depth is the ceiling.
        public void EnqueueLinks(
            HitContext parent,
            IReadOnlyList<ICombatant> targets,
            List<IHitEffect> effects,
            HashSet<ICombatant> sharedAlreadyHit)
        {
            if (targets == null || effects == null) return;

            int nextDepth = parent.ChainDepth + 1;
            // parent.MaxChainDepth == 0 means "no chaining"; a depth-1 link is already
            // past it, so a weapon that hasn't opted in never cascades.
            if (nextDepth > parent.MaxChainDepth) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || t.IsDying) continue;
                if (sharedAlreadyHit.Contains(t)) continue;
                sharedAlreadyHit.Add(t);

                queue.Enqueue(pool.RentLink(parent, t, effects, sharedAlreadyHit));
            }
        }

        // Drain everything queued, and everything those resolutions queue in turn.
        // Called by the resolver at the TOP LEVEL only. The `draining` guard stops a
        // hit resolved mid-drain (itself a top-level ResolveHit) from starting a second
        // drain — that guard is separate from the resolver's own re-entrancy depth,
        // which guards the effect buffer. Both are needed; see the resolver edit notes.
        //
        // Count is re-read each iteration: a link that kills can enqueue its own hops
        // mid-drain (the cascade loop), draining in the same pass. Termination is the
        // shared set plus the depth cap, not a snapshot.
        public void Drain()
        {
            if (draining) return;
            draining = true;
            try
            {
                while (queue.Count > 0)
                {
                    var ctx = queue.Dequeue();
                    try { resolver.ResolveHit(ctx); }
                    finally { pool.Return(ctx); }
                }
            }
            finally
            {
                draining = false;
            }
        }

        public bool IsDraining => draining;
    }
}