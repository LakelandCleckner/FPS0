using System.Collections.Generic;

namespace Combat.Core
{
    // Pool of HitContext objects for MULTI-TARGET hits — AoE blasts (a rocket, an
    // explosion) and chain links (lightning arcs, explosion cascades). A single
    // delivery or reaction can spawn many contexts in one frame, so they are reused
    // rather than allocated per hit, the same discipline the status pool and
    // projectiles already follow.
    //
    // TWO SEEDING MODES, because AoE and chains are different:
    //   Sibling — all targets struck by ONE delivery. Same depth as the parent, no
    //             added falloff, full damage to each. A rocket blast: everyone in
    //             radius takes a full direct hit.
    //   Link    — successive hops of a cascade. Depth increments, falloff compounds.
    //             Lightning arcing target to target, or an explosion that chains.
    //
    // REUSE HAZARD: a rented context has fields left from its previous use. Renting
    // BUMPS THE GENERATION and FULLY RESETS every field the resolver or an effect
    // writes — the WasCrit failure class. The reset list is audited by
    // ContextResetAudit (the "multi-target link" site); adding a field to HitContext
    // fails that audit until it is classified.
    //
    // Contexts are returned after their resolution completes; the pool grows on demand
    // rather than dropping a hit, because a dropped hit is an invisible correctness bug
    // while a grown pool is just memory.
    public class ChainContextPool
    {
        private readonly Stack<HitContext> free = new Stack<HitContext>(16);

        // A sibling hit: same depth as the parent. Every target of one AoE delivery
        // is a sibling of the others — all full damage (ChainMultiplier is derived
        // from ChainDepth, and at the parent's depth it's unchanged).
        public HitContext RentSibling(
            HitContext parent,
            ICombatant target,
            List<IHitEffect> effects,
            HashSet<ICombatant> sharedAlreadyHit)
            => Rent(parent, target, effects, sharedAlreadyHit, depth: parent.ChainDepth);

        // A chain link: one hop deeper. ChainMultiplier is a COMPUTED property on
        // HitContext (Pow(ChainFalloff, depth) * Pow(ChainGrowth, depth)), so simply
        // setting the deeper ChainDepth makes the bolt weaken per hop automatically —
        // there is no multiplier field to seed.
        public HitContext RentLink(
            HitContext parent,
            ICombatant target,
            List<IHitEffect> effects,
            HashSet<ICombatant> sharedAlreadyHit)
            => Rent(parent, target, effects, sharedAlreadyHit, depth: parent.ChainDepth + 1);

        private HitContext Rent(
            HitContext parent,
            ICombatant target,
            List<IHitEffect> effects,
            HashSet<ICombatant> sharedAlreadyHit,
            int depth)
        {
            var ctx = free.Count > 0 ? free.Pop() : new HitContext();
            ctx.BumpGeneration();

            // ---- carried from the parent: the delivery/cascade identity ----
            ctx.Attacker = parent.Attacker;
            ctx.DamageSource = parent.DamageSource;
            ctx.SourceFaction = parent.SourceFaction;
            ctx.MaxChainDepth = parent.MaxChainDepth;
            ctx.ChainFalloff = parent.ChainFalloff;
            ctx.ChainGrowth = parent.ChainGrowth;
            ctx.DedupMode = parent.DedupMode;

            // ---- this hit ----
            // Source is Chain for a link. For a sibling AoE hit it's the parent's
            // Source: a rocket's blast targets are DIRECT hits (base damage, not a
            // chain), so they read as Direct and a Direct-only perk fires on them. A
            // sibling seeded from an already-Chain parent stays Chain. That's the
            // reframe — AoE is a delivery shape, not a chain.
            ctx.Source = depth > parent.ChainDepth ? HitSource.Chain : parent.Source;
            ctx.ChainDepth = depth;   // ChainMultiplier derives from this automatically
            ctx.Target = target;
            ctx.Effects = effects;

            // Shared dedup set, referenced not cloned — so every hit of one delivery
            // (or one cascade) dedups against the same targets while separate ones stay
            // independent. Seeding it is the caller's job (a blast may pre-seed the
            // direct-hit victim so the splash doesn't double-hit them).
            ctx.ShareAlreadyHit(sharedAlreadyHit);

            // ---- per-hit data with no natural value for a splash/arc target ----
            ctx.HitPoint = target is UnityEngine.MonoBehaviour mb
                ? mb.transform.position
                : parent.HitPoint;
            ctx.HitboxMultiplier = 1f;   // no headshot on a splash target
            ctx.BodyPartHit = BodyPart.Torso;
            ctx.DamageType = parent.DamageType;
            ctx.Shot = default;

            // DamageHitEffect delivers its (already defense-applied) damage through
            // ApplyStatusTickDamage. A direct hit gets that delegate from the struck
            // EnemyHitbox; a chain link has only the combatant (found by overlap, no
            // hitbox), so route to the combatant's own TakeDamage as a Torso hit —
            // splash and arc targets have no precision. Without this the link would
            // COMPUTE damage and the ?. would silently drop it (no health removed,
            // but DamageDealt/WasKill still set — the worst kind of half-working).
            ctx.ApplyDamageToTarget = null;
            ctx.ApplyStatusTickDamage = target != null
                ? (dmg, type) => target.TakeDamage(dmg, BodyPart.Torso, type)
                : (System.Action<float, DamageTypeSO>)null;
            ctx.SourceStatus = null;
            ctx.ShowFloatingNumber = true;
            ctx.FeedsAccumulator = false;

            // ---- results, reset so nothing leaks from the previous use ----
            ctx.DamageDealt = 0f;
            ctx.WasKill = false;
            ctx.WasHeadshot = false;
            ctx.WasDebuffed = false;
            ctx.WasCrit = false;        // RollCrit resets these too, but a rented
            ctx.CritMultiplier = 1f;    // context must never carry stale results.
            return ctx;
        }

        public void Return(HitContext ctx)
        {
            if (ctx == null) return;
            ctx.Target = null;
            ctx.Effects = null;
            ctx.ApplyStatusTickDamage = null;
            ctx.ClearSharedAlreadyHit();
            free.Push(ctx);
        }
    }
}