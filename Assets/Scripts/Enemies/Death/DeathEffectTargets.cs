using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Feedback;
using Combat.Hazards;

namespace Combat.Enemies
{
    // Finds and hits the player and enemies around a death effect.
    public static class DeathEffectTargets
    {
        public static void Find(Vector3 centre, float radius, EnemyDeathEffectSO so, ICombatant owner, List<HazardTargets.Target> results)
            => HazardTargets.InSphere(centre, radius, so.PlayerMask, so.EnemyMask, owner, results);

        // One hit through the dead enemy's resolver, so resistances, statuses and HUD all apply.
        // Returns the damage dealt.
        public static float Hit(in DeathEffectContext src, EnemyDeathEffectSO so, HazardTargets.Target t, List<IHitEffect> effects, DamageTypeSO type)
        {
            if (src.Resolver == null || t.Combatant == null || t.Combatant.IsDying) return 0f;

            var victim = t.Combatant;
            var ctx = new HitContext
            {
                Target = victim,
                HitPoint = t.Point,
                SourceFaction = src.Faction,
                DamageType = type,
                Source = HitSource.Direct,
                BodyPartHit = BodyPart.Torso,
                ApplyStatusTickDamage = (d, dt) => victim.TakeDamage(d, BodyPart.Torso, dt),
                Attacker = src.Owner,
                Effects = effects,
                DedupMode = HitDedupMode.PerShot,
                ShowFloatingNumber = false
            };

            if (t.Hurtbox != null)
            {
                t.Hurtbox.Receive(src.Resolver, ctx, src.Owner != null ? src.Owner.transform : null);
                return ctx.DamageDealt;
            }

            src.Resolver.ResolveHit(ctx);
            if (so.showDamageNumbers && ctx.DamageDealt > 0f && DamageNumberPool.Instance != null)
            {
                var mb = victim as MonoBehaviour;
                Vector3 pos = mb != null ? mb.transform.position + Vector3.up * 2f : t.Point;
                DamageNumberPool.Instance.Spawn(pos, ctx.DamageDealt, type, false, ctx.WasDebuffed);
            }
            return ctx.DamageDealt;
        }
    }
}
