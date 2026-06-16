using Combat.Core;

namespace Combat.Effects
{
    // WORKING SLICE: flat damage that respects the hitbox multiplier (crit/headshot)
    // carried on the context, the target's composed defensive multiplier (type +
    // body-part resistance + future layers), and chain scaling. Writes the FINAL
    // post-everything value back so feedback (damage numbers) always matches what
    // actually lands
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

            // DEFENSE: target composes all its resistance layers into one multiplier
            final *= ctx.Target.GetDamageMultiplier(spec.Type, ctx.BodyPartHit);

            // deal the final value; TakeDamage now just subtracts it
            ctx.ApplyStatusTickDamage?.Invoke(final, spec.Type);

            // write results for feedback — DamageDealt == what actually landed
            ctx.DamageDealt += final;
            ctx.WasHeadshot = ctx.BodyPartHit == BodyPart.Head;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;

        }
    }
}
