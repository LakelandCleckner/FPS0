using UnityEngine;
using Combat.Core;
using Combat.Stats;

namespace Combat.Effects
{
    // Damage from a DamageSpec, multiplied through the resolver's damage formula.
    // Derivation resolves through a DerivationContext (attacker/source/target), so a
    // spec can scale off any participant's stats or health quantities.
    public class DamageHitEffect : IHitEffect
    {
        public EffectPhase Phase => EffectPhase.Application;
        public bool PropagatesOnChain => true;

        private DamageSpec spec;

        public DamageHitEffect(DamageSpec spec) { this.spec = spec; }

        // Reused effects (chain arcs) swap their spec per use.
        public void SetSpec(DamageSpec value) => spec = value;

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            var dctx = new DerivationContext(ctx.Attacker, ctx.DamageSource, ctx.Target);
            float raw = spec.Resolve(in dctx);

            // Every multiplier comes from the resolver's damage formula.
            float final = DamageFormulaSO.Apply(resolver != null ? resolver.Formula : null, raw, ctx,
                                                spec.Type, spec.AffectedByChainFalloff);

            if (ctx.ApplyStatusTickDamage != null)
            {
                ctx.ApplyStatusTickDamage(final, spec.Type);
                if (ctx.Target is CombatantStats target && target.Health != null)
                    target.Health.ReportHit(new HitReport(final, ctx.BodyPartHit, spec.Type, ctx.Source,
                                                          ctx.HitPoint, HitDirection(ctx), StaggerPower(ctx)));
            }

            ctx.DamageDealt += final;
            ctx.WasHeadshot = ctx.BodyPartHit == BodyPart.Head;
            ctx.WasDebuffed = ctx.Target != null && ctx.Target.IsDebuffed;
            ctx.WasKill = ctx.Target.CurrentHealth <= 0f;
        }

        // Shot origin to impact, else attacker to impact
        private static Vector3 HitDirection(HitContext ctx)
        {
            Vector3 from;
            if (ctx.Shot.HasOrigin) from = ctx.Shot.Origin;
            else if (ctx.Attacker is Component c && c != null) from = c.transform.position;
            else return Vector3.zero;
            Vector3 d = ctx.HitPoint - from;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
        }

        private static float StaggerPower(HitContext ctx)
            => ctx.DamageSource is IStaggerSource s ? s.StaggerPower : 1f;
    }
}