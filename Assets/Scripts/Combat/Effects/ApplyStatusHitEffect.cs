using Combat.Core;
using Combat.Status;
using UnityEngine;

namespace Combat.Effects
{
    // Bridges the on-hit effect list into the status system. Phase 2i-b: passes the
    // attacker (ICombatant) and source so the status's ticks can derive off any
    // participant and can crit.
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

            System.Action<float> applyTick = null;
            if (ctx.ApplyStatusTickDamage != null)
                applyTick = (dmg) => ctx.ApplyStatusTickDamage(dmg, tickType);

            receiver.Apply(
                status: statusDef,
                resolver: resolver,
                attacker: ctx.Attacker,
                source: ctx.DamageSource,
                tickType: tickType,
                sourceFaction: ctx.SourceFaction,
                chainDepth: 0,
                chainMultiplier: ctx.ChainMultiplier,
                applyTickDamage: applyTick);
        }
    }
}