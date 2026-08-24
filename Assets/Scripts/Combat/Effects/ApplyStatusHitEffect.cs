using Combat.Core;
using Combat.Status;
using UnityEngine;

namespace Combat.Effects
{
    // Bridges the on-hit effect list into the status system. Phase 2i-b: passes the
    // attacker (ICombatant) and source so the status's ticks can derive off any
    // participant and can crit.
    //
    // DELEGATE LIFETIME (fixed): the tick delegate binds to the TARGET, never to the
    // applying HitContext. A direct hit's context is long-lived, but an AoE/chain
    // context is pooled and RETURNED the instant the blast finishes draining — its
    // ApplyStatusTickDamage is nulled and the object recycled for the next blast. A
    // delegate closing over that context ticked ONCE (during application, while the
    // context was still valid) and then silently did nothing, because every later tick
    // called a nulled delegate on a recycled context.
    //
    // The target outlives every status it carries, so binding to it is correct no
    // matter how the applying context is managed — this would also protect the direct
    // path the day its context becomes pooled.
    public class ApplyStatusHitEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.StatusApplication;
        public bool PropagatesOnChain => true;

        private readonly StatusSO statusDef;

        public ApplyStatusHitEffect(StatusSO statusDef) { this.statusDef = statusDef; }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            if (ctx.Target == null || ctx.Target.IsDying) return;

            var receiver = (ctx.Target as MonoBehaviour)?.GetComponent<StatusReceiver>();
            if (receiver == null) return;

            var tickType = statusDef.damageType;

            // Bind to the TARGET, not ctx. See the lifetime note above — closing over a
            // pooled AoE/chain context is what made an explosion-applied DOT tick once
            // and stop. Uses ICombatant.TakeDamage (Torso — a tick has no precision),
            // which respects the target's defense/type the same way the old hitbox
            // delegate did.
            var victim = ctx.Target;
            System.Action<float> applyTick = (dmg) => victim.TakeDamage(dmg, BodyPart.Torso, tickType);

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