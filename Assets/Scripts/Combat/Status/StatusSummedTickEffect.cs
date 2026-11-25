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
        public EffectPhase Phase => EffectPhase.StatusApplication;
        public bool PropagatesOnChain => false;

        // Not readonly: the owning EffectStackPool reuses one instance across ticks
        // rather than allocating a fresh effect every tick.
        private float summedWeight;
        private readonly DamageTypeSO type;
        private readonly System.Action<float> applyDamage;

        public void SetTickDamage(float weight) => summedWeight = weight;

        public StatusSummedTickEffect(float summedWeight, DamageTypeSO type,
                                      System.Action<float> applyDamage)
        {
            this.summedWeight = summedWeight;
            this.type = type;
            this.applyDamage = applyDamage;
        }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            // Crit (this tick's own roll), defense and the rest come from the resolver's damage formula
            float final = DamageFormulaSO.Apply(resolver != null ? resolver.Formula : null, summedWeight, ctx, type, false);

            applyDamage?.Invoke(final);

            ctx.DamageDealt += final;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
            // WasCrit set by the resolver's roll; WasHeadshot stays false for ticks.
            ctx.WasDebuffed = ctx.Target != null && ctx.Target.IsDebuffed;
        }
    }
}