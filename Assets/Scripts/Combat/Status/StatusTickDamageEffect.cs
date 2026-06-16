using Combat.Core;

namespace Combat.Status
{
    // The damage application for one status tick, expressed as an IHitEffect so
    // it flows through the resolver like any other effect (gets feedback, writes
    // results). Applies the target's composed defensive multiplier so ticks are
    // resisted consistently, and writes the FINAL value so the tick's damage
    // number matches what actually lands.
    public class StatusTickDamageEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Application;
        public bool PropagatesOnChain => false;

        private readonly DamageSpec spec;
        private readonly System.Action<float> applyDamage;

        public StatusTickDamageEffect(DamageSpec spec, System.Action<float> applyDamage)
        {
            this.spec = spec;
            this.applyDamage = applyDamage;
        }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            float raw = spec.ComputeRaw(ctx.Stats, ctx.Target);
            // no chain falloff at a tick's origin (depth 0)
            // DEFENSE: same composed multiplier as direct hits (ticks have no
            // body part, so HitboxMultiplier/bodyPart are neutral here)
            float final = raw * ctx.Target.GetDamageMultiplier(spec.Type, ctx.BodyPartHit);
            applyDamage?.Invoke(final);

            ctx.DamageDealt += final;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
            // WasHeadshot / WasCrit stay false for ticks (inert hooks for later)
        }
    }

}

