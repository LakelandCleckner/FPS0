using Combat.Core;

namespace Combat.Effects
{
    // WORKING SLICE: flat damage that respects the hitbox multiplier (crit/headshot)
    // carried on the context. Writes results back for feedback to read.
    public class DamageHitEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Application;
        public bool PropagatesOnChain => true;

        private readonly DamageSpec spec;

        public DamageHitEffect(DamageSpec spec) { this.spec = spec; }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            // raw from immutable stat block (flat for the slice)
            float raw = spec.ComputeRaw(ctx.Stats, ctx.Target);

            // apply the per-hit multiplier the hitbox supplied (headshot crit)
            float final = raw * ctx.HitboxMultiplier;

            // chain scaling — no-op at depth 0, here for when chains land later
            if (spec.AffectedByChainFalloff)
                final *= ctx.ChainMultiplier;

            // deal it through your existing EnemyHealth.TakeDamage
            ctx.ApplyDamageToTarget(final);

            // write results for feedback
            ctx.DamageDealt += final;
            ctx.WasHeadshot = ctx.BodyPartHit == BodyPart.Head;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
        }
    }
}
