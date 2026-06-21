using Combat.Core;

namespace Combat.Status
{
    // A status pool's tick damage, already SUMMED from the pool's entries,
    // expressed as an IHitEffect so it flows through the resolver (feedback +
    // chain-ready). Applies the target's composed defensive multiplier so the
    // tick is resisted, and writes the final value to DamageDealt so the damage
    // number / accumulator match what landed.
    //
    // Replaces StatusTickDamageEffect's role for the pooled-stacking model: the
    // pool pre-sums entry weights, this effect applies that sum (with defense).
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
            // DEFENSE: composed multiplier (type + body-part + future layers).
            // ticks have no body part, so bodyPart is neutral here.
            float final = summedWeight * ctx.Target.GetDamageMultiplier(type, ctx.BodyPartHit);

            applyDamage?.Invoke(final);

            ctx.DamageDealt += final;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
            // WasHeadshot / WasCrit stay false for ticks (inert hooks for later)
        }
    }
}
