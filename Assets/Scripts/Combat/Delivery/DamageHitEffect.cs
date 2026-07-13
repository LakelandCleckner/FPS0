using Combat.Core;

namespace Combat.Effects
{
    // Damage that respects: precision (hitbox) multiplier, the CRIT multiplier
    // (rolled once at the resolver), chain scaling, and the target's composed
    // defensive multiplier. Writes the FINAL value back so feedback matches.
    public class DamageHitEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Application;
        public bool PropagatesOnChain => true;

        private readonly DamageSpec spec;

        public DamageHitEffect(DamageSpec spec) { this.spec = spec; }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            float raw = spec.ComputeRaw(ctx.Stats, ctx.Target);

            // precision (hitbox/headshot)
            float final = raw * ctx.HitboxMultiplier;

            // CRIT — rolled at the resolver; 1f when no crit. Independent of
            // precision: a precision crit gets BOTH.
            final *= ctx.CritMultiplier;

            // chain scaling — no-op at depth 0
            if (spec.AffectedByChainFalloff)
                final *= ctx.ChainMultiplier;

            // DEFENSE
            final *= ctx.Target.GetDamageMultiplier(spec.Type, ctx.BodyPartHit);

            ctx.ApplyStatusTickDamage?.Invoke(final, spec.Type);

            ctx.DamageDealt += final;
            ctx.WasHeadshot = ctx.BodyPartHit == BodyPart.Head;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
        }
    }
}