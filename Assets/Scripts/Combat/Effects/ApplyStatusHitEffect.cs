using Combat.Core;
using Combat.Status;
using UnityEngine;

namespace Combat.Effects
{
    // Bridges the on-hit effect list into the status system. When this runs in
    // the resolver, it tells the target's StatusReceiver to apply a status,
    // building a StatusInstance from the hit's snapshot.
    //
    // Stateless (Application phase) — follows the cached-singleton contract.
    public class ApplyStatusHitEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Application;
        public bool PropagatesOnChain => true;

        private readonly StatusSO statusDef;

        public ApplyStatusHitEffect(StatusSO statusDef) { this.statusDef = statusDef; }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            if (ctx.Target == null || ctx.Target.IsDying) return;

            var receiver = (ctx.Target as MonoBehaviour)?.GetComponent<StatusReceiver>();
            if (receiver == null) return;

            // The tick damage callback. We reuse the hit's ApplyStatusTickDamage
            // path so the tick's type (statusDef.damageType) reaches TakeDamage
            // and resistance applies. Captured here from the applying context.
            var tickType = statusDef.damageType;
            var applyTick = BuildTickCallback(ctx, tickType);


            var instance = new StatusInstance();
            instance.Init(
                target:        ctx.Target,
                resolver:      resolver,
                stats:         ctx.Stats,
                tickSpec:      statusDef.BuildTickSpec(),
                carriedEffects: ctx.Effects,
                sourceFaction: ctx.SourceFaction,
                tickType:      tickType,
                duration:      statusDef.duration,
                tickInterval:  statusDef.tickInterval,
                applyTickDamage: applyTick);

            receiver.Apply(instance);
        }

        // Builds a callback that applies typed damage to the same target the hit
        // landed on. Uses the context's typed tick-damage hook if present.
        private System.Action<float> BuildTickCallback(HitContext ctx, DamageTypeSO tickType)
        {
            if (ctx.ApplyStatusTickDamage != null)
                return (dmg) => ctx.ApplyStatusTickDamage(dmg, tickType);
            return null;
        }

    }
}
