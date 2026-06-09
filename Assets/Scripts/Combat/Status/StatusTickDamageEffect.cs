using Combat.Core;

namespace Combat.Status
{
    // The damage application for one status tick, expressed as an IHitEffect so
    // it flows through the resolver like any other effect (gets feedback, writes
    // results). Computes from the snapshot spec, applies via the tick callback,
    // writes results so feedback reflects the tick.
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
            // no chain falloff at a tick's origin (depth 0); resistance applies
            // at the target's damage chokepoint via the callback
            applyDamage?.Invoke(raw);

            ctx.DamageDealt += raw;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
            // WasHeadshot / WasCrit stay false for ticks (inert hooks for later)
        }
    }
}
