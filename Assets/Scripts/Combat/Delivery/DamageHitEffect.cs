using Combat.Core;

namespace Combat.Effects
{
    // Damage that respects precision (hitbox), CRIT, chain scaling, and the target's
    // composed defense. Phase 2i-b: derivation resolves through a DerivationContext
    // (attacker/source/target), so a spec can scale off any participant's stats or
    // health quantities.
    public class DamageHitEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Application;
        public bool PropagatesOnChain => true;

        private readonly DamageSpec spec;

        public DamageHitEffect(DamageSpec spec) { this.spec = spec; }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            var dctx = new DerivationContext(ctx.Attacker, ctx.DamageSource, ctx.Target);
            float raw = spec.Resolve(in dctx);

            float final = raw * ctx.HitboxMultiplier;   // precision
            final *= ctx.CritMultiplier;                 // crit (rolled at resolver)

            if (spec.AffectedByChainFalloff)
                final *= ctx.ChainMultiplier;

            final *= ctx.Target.GetDamageMultiplier(spec.Type, ctx.BodyPartHit);  // defense

            ctx.ApplyStatusTickDamage?.Invoke(final, spec.Type);

            ctx.DamageDealt += final;
            ctx.WasHeadshot = ctx.BodyPartHit == BodyPart.Head;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
        }
    }
}