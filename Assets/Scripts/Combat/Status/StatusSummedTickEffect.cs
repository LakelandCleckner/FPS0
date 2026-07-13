using Combat.Core;

namespace Combat.Status
{
    // A status pool's tick damage (already summed from live-linked entry weights),
    // expressed as an IHitEffect so it flows through the resolver.
    //
    // Phase 2h: applies the CRIT multiplier the resolver rolled for THIS tick. Each
    // tick rolls independently, so a burn can crit on some ticks and not others.
    public class StatusSummedTickEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Application;
        public bool PropagatesOnChain => false;

        private readonly float summedWeight;
        private readonly DamageTypeSO type;
        private readonly System.Action<float> applyDamage;

        public StatusSummedTickEffect(float summedWeight, DamageTypeSO type,
                                      System.Action<float> applyDamage)
        {
            this.summedWeight = summedWeight;
            this.type = type;
            this.applyDamage = applyDamage;
        }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            // CRIT — this tick's own roll (1f if it didn't crit)
            float final = summedWeight * ctx.CritMultiplier;

            // DEFENSE: composed multiplier (type + body-part + future layers)
            final *= ctx.Target.GetDamageMultiplier(type, ctx.BodyPartHit);

            applyDamage?.Invoke(final);

            ctx.DamageDealt += final;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
            // WasCrit set by the resolver's roll; WasHeadshot stays false for ticks.
        }
    }
}