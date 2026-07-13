using Combat.Core;
using Combat.Status;
using UnityEngine;

namespace Combat.Effects
{
    // Bridges the on-hit effect list into the status system.
    //
    // Phase 2h: passes the SOURCE (so the entry live-links to it and derives its
    // weight from current cached stats) and the ATTACKER's stats (so each tick can
    // roll crit).
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
                source: ctx.DamageSource,            // live link for weight derivation
                snapshotStats: ctx.Stats,            // fallback if the source dies
                attackerStats: ctx.AttackerStats,    // crit stats for tick rolls
                tickType: tickType,
                sourceFaction: ctx.SourceFaction,
                chainDepth: 0,
                chainMultiplier: ctx.ChainMultiplier,  // frozen for THIS application
                applyTickDamage: applyTick);
        }
    }
}