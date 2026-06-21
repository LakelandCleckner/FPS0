using Combat.Core;
using Combat.Status;
using UnityEngine;

namespace Combat.Effects
{
    // Bridges the on-hit effect list into the status system. Tells the target's
    // StatusReceiver to apply a status, which adds an entry to the (target,
    // StatusSO) pool. STAGE 2: pool-based.
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

            var tickType = statusDef.damageType;

            // tick damage callback — routes the (already-summed, post-defense)
            // tick value to the same target this hit landed on, carrying type so
            // the chokepoint logging/feedback is correct
            System.Action<float> applyTick = null;
            if (ctx.ApplyStatusTickDamage != null)
                applyTick = (dmg) => ctx.ApplyStatusTickDamage(dmg, tickType);

            receiver.Apply(
                status: statusDef,
                resolver: resolver,
                stats: ctx.Stats,
                tickType: tickType,
                sourceFaction: ctx.SourceFaction,
                chainDepth: 0,           // chain links set this later
                applyTickDamage: applyTick);
        }
    }
}