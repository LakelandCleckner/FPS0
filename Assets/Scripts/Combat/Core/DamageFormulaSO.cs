using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Events;

namespace Combat.Core
{
    // One multiplier in the damage formula.
    public enum DamageLayer
    {
        Hitbox,          // the struck hitbox's multiplier
        Crit,            // 1 + crit damage when the hit crit
        Condition,       // hit-condition perk bucket
        PelletShare,     // a pellet's share of the shot
        ElementIdentity, // the damage type's direct-hit multiplier
        Precision,       // weapon precision multiplier on head hits
        RangeFalloff,    // weapon range falloff
        ChainFalloff,    // chain link falloff, when the damage uses it
        ElementDamage,   // the attacker's damage stat for the damage's element
        Defense          // the target's resistances, body part and damage taken
    }

    [Serializable]
    public class DamageFormulaLayer
    {
        public DamageLayer layer;
        [Tooltip("Which hits this layer applies to.")]
        public HitSourceMask appliesTo = HitSourceMask.All;
    }

    // The ordered multipliers applied to raw damage. Player and enemy resolvers each point at one.
    [CreateAssetMenu(fileName = "DamageFormula", menuName = "Combat/Damage Formula")]
    public class DamageFormulaSO : ScriptableObject
    {
        public List<DamageFormulaLayer> layers = DefaultLayers();

        private static readonly List<DamageFormulaLayer> fallback = DefaultLayers();

        // The formula used when a resolver has none assigned.
        public static List<DamageFormulaLayer> DefaultLayers()
        {
            const HitSourceMask all = HitSourceMask.All, direct = HitSourceMask.Direct;
            return new List<DamageFormulaLayer>
            {
                new DamageFormulaLayer { layer = DamageLayer.Hitbox, appliesTo = all },
                new DamageFormulaLayer { layer = DamageLayer.Crit, appliesTo = all },
                new DamageFormulaLayer { layer = DamageLayer.Condition, appliesTo = all },
                new DamageFormulaLayer { layer = DamageLayer.PelletShare, appliesTo = direct },
                new DamageFormulaLayer { layer = DamageLayer.ElementIdentity, appliesTo = direct },
                new DamageFormulaLayer { layer = DamageLayer.Precision, appliesTo = direct },
                new DamageFormulaLayer { layer = DamageLayer.RangeFalloff, appliesTo = direct },
                new DamageFormulaLayer { layer = DamageLayer.ChainFalloff, appliesTo = all },
                new DamageFormulaLayer { layer = DamageLayer.ElementDamage, appliesTo = all },
                new DamageFormulaLayer { layer = DamageLayer.Defense, appliesTo = all },
            };
        }

        // raw x every layer that applies to this hit. type = the damage's own type (defense, element damage).
        public static float Apply(DamageFormulaSO formula, float raw, HitContext ctx, DamageTypeSO type, bool chainFalloff)
        {
            var list = formula != null ? formula.layers : fallback;
            var source = ctx.Source.ToMask();
            float value = raw;
            for (int i = 0; i < list.Count; i++)
            {
                var l = list[i];
                if (l == null || (l.appliesTo & source) == 0) continue;
                value *= Factor(l.layer, ctx, type, chainFalloff);
            }
            return value;
        }

        public static float Factor(DamageLayer layer, HitContext ctx, DamageTypeSO type, bool chainFalloff)
        {
            switch (layer)
            {
                case DamageLayer.Hitbox: return ctx.HitboxMultiplier;
                case DamageLayer.Crit: return ctx.CritMultiplier;
                case DamageLayer.Condition: return ctx.ConditionMultiplier;
                case DamageLayer.PelletShare: return ctx.Shot.DamageScale;
                case DamageLayer.ElementIdentity:
                    return ctx.DamageType != null ? ctx.DamageType.directDamageMultiplier : 1f;
                case DamageLayer.Precision:
                    if (ctx.BodyPartHit != BodyPart.Head || !(ctx.DamageSource is IPrecisionSource precise)) return 1f;
                    return precise.PrecisionMultiplier + (ctx.DamageType != null ? ctx.DamageType.precisionBonus : 0f);
                case DamageLayer.RangeFalloff:
                    if (!ctx.Shot.HasOrigin || !(ctx.DamageSource is IRangeFalloffSource falloff)) return 1f;
                    return falloff.FalloffMultiplier(Vector3.Distance(ctx.HitPoint, ctx.Shot.Origin), ctx.Shot.AimBlend);
                case DamageLayer.ChainFalloff: return chainFalloff ? ctx.ChainMultiplier : 1f;
                case DamageLayer.ElementDamage: return ElementDamage(ctx, type);
                case DamageLayer.Defense:
                    return ctx.Target != null ? ctx.Target.GetDamageMultiplier(type, ctx.BodyPartHit) : 1f;
                default: return 1f;
            }
        }

        // The attacker's damage stat for this element (base 1). 1 when there is no stat or no attacker.
        private static float ElementDamage(HitContext ctx, DamageTypeSO type)
        {
            var t = type != null ? type : ctx.DamageType;
            var stats = ctx.Attacker != null ? ctx.Attacker.Stats : null;
            if (t == null || t.damageStat == null || stats == null) return 1f;
            return Mathf.Max(0f, stats.Resolve(t.damageStat));
        }

        // Setup problems, one readable line each.
        public List<string> Problems()
        {
            var problems = new List<string>();
            var seen = new HashSet<DamageLayer>();
            foreach (var l in layers)
            {
                if (l == null) { problems.Add("Empty layer slot."); continue; }
                if (!seen.Add(l.layer)) problems.Add($"{l.layer} is listed more than once, so it applies twice.");
                if (l.appliesTo == HitSourceMask.None) problems.Add($"{l.layer} applies to no hits.");
            }
            foreach (DamageLayer d in Enum.GetValues(typeof(DamageLayer)))
                if (!seen.Contains(d)) problems.Add($"{d} is missing, so it never applies.");
            return problems;
        }
    }
}
