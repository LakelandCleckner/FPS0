using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Stats;

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
        Defense,         // the target's resistances, body part and damage taken
        Stat             // any stat, read from the attacker, source or target
    }

    // How a Stat layer turns its stat into a multiplier.
    public enum StatLayerMode
    {
        Value,  // the stat is the multiplier (base 1)
        OnePlus // 1 + the stat (base 0)
    }

    [Serializable]
    public class DamageFormulaLayer
    {
        public DamageLayer layer;
        [Tooltip("Which hits this layer applies to.")]
        public HitSourceMask appliesTo = HitSourceMask.All;

        [Header("Stat layer")]
        [Tooltip("Name shown in tools. Empty = the stat's name.")]
        public string label = "";
        public StatDefinitionSO stat;
        [Tooltip("Whose stats the value is read from.")]
        public StatScope owner = StatScope.Attacker;
        public StatLayerMode mode = StatLayerMode.Value;
        [Tooltip("Only hits of this damage type. Empty = every type.")]
        public DamageTypeSO onlyType;

        public string Name
        {
            get
            {
                if (layer != DamageLayer.Stat) return layer.ToString();
                if (!string.IsNullOrWhiteSpace(label)) return label;
                if (stat == null) return "Stat (none)";
                return string.IsNullOrWhiteSpace(stat.displayName) ? stat.name : stat.displayName;
            }
        }

        // Layers that can sit in a formula more than once (each Stat layer is its own member).
        public bool Repeatable => layer == DamageLayer.Stat;
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
                value *= l.layer == DamageLayer.Stat ? StatFactor(l, ctx, type) : Factor(l.layer, ctx, type, chainFalloff);
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

        // A Stat layer's multiplier for this hit; 1 when it doesn't apply or can't be read.
        public static float StatFactor(DamageFormulaLayer l, HitContext ctx, DamageTypeSO type)
        {
            if (l.stat == null) return 1f;
            if (l.onlyType != null && l.onlyType != (type != null ? type : ctx.DamageType)) return 1f;

            StatContainer c = null;
            switch (l.owner)
            {
                case StatScope.Attacker: c = ctx.Attacker != null ? ctx.Attacker.Stats : null; break;
                case StatScope.Source: c = ctx.DamageSource != null ? ctx.DamageSource.SourceStats : null; break;
                case StatScope.Target: c = ctx.Target != null ? ctx.Target.Stats : null; break;
            }
            if (c == null) return 1f;
            return Mult(l.mode, c.Resolve(l.stat));
        }

        public static float Mult(StatLayerMode mode, float value)
            => Mathf.Max(0f, mode == StatLayerMode.OnePlus ? 1f + value : value);

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
            var stats = new HashSet<(StatDefinitionSO, StatScope)>();
            foreach (var l in layers)
            {
                if (l == null) { problems.Add("Empty layer slot."); continue; }
                if (l.appliesTo == HitSourceMask.None) problems.Add($"{l.Name} applies to no hits.");
                if (l.Repeatable)
                {
                    if (l.stat == null) problems.Add("A Stat layer has no stat, so it does nothing.");
                    else if (!stats.Add((l.stat, l.owner))) problems.Add($"{l.Name} ({l.owner}) is listed more than once, so it applies twice.");
                    continue;
                }
                if (!seen.Add(l.layer)) problems.Add($"{l.layer} is listed more than once, so it applies twice.");
            }
            foreach (DamageLayer d in Enum.GetValues(typeof(DamageLayer)))
                if (d != DamageLayer.Stat && !seen.Contains(d)) problems.Add($"{d} is missing, so it never applies.");
            return problems;
        }
    }
}
