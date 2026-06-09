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

            var instance = new StatusInstance();
            instance.Init(
                target:        ctx.Target,
                resolver:      resolver,
                stats:         ctx.Stats,
                tickSpec:      statusDef.BuildTickSpec(),
                carriedEffects: ctx.Effects,
                sourceFaction: ctx.SourceFaction,
                duration:      statusDef.duration,
                tickInterval:  statusDef.tickInterval,
                applyDamage:   ctx.ApplyDamageToTarget != null
                    ? (dmg, type) => ctx.ApplyStatusTickDamage(dmg, type)
                    : (System.Action<float, DamageTypeSO>)null);

            receiver.Apply(instance);
        }
    }
}
