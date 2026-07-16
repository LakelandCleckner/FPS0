using UnityEngine;
using Combat.Stats;

namespace Combat.Core
{
    // Describes HOW to compute a damage number (data-driven). A derivation names a
    // KIND (Flat / PercentOfStat / PercentOfQuantity), an OWNER scope, and the stat
    // or quantity to read. Resolved against a DerivationContext.
    public readonly struct DamageSpec
    {
        public readonly DerivationKind Kind;
        public readonly float Coefficient;      // 0.5 = 50% of what the derivation points at
        public readonly DamageTypeSO Type;
        public readonly StatScope Owner;        // for PercentOfStat / PercentOfQuantity

        // payload — only the one matching Kind is used
        public readonly StatDefinitionSO Stat;      // PercentOfStat
        public readonly QuantityKind Quantity;      // PercentOfQuantity

        public readonly bool AffectedByChainFalloffOverride;
        public readonly bool HasOverride;

        // Flat
        public DamageSpec(float flatCoefficient, DamageTypeSO type,
                          bool? affectedByChainFalloff = null)
        {
            Kind = DerivationKind.Flat;
            Coefficient = flatCoefficient;
            Type = type;
            Owner = StatScope.Source;
            Stat = null;
            Quantity = default;
            HasOverride = affectedByChainFalloff.HasValue;
            AffectedByChainFalloffOverride = affectedByChainFalloff ?? false;
        }

        // PercentOfStat
        public DamageSpec(StatDefinitionSO stat, StatScope owner, float coefficient,
                          DamageTypeSO type, bool? affectedByChainFalloff = null)
        {
            Kind = DerivationKind.PercentOfStat;
            Coefficient = coefficient;
            Type = type;
            Owner = owner;
            Stat = stat;
            Quantity = default;
            HasOverride = affectedByChainFalloff.HasValue;
            AffectedByChainFalloffOverride = affectedByChainFalloff ?? false;
        }

        // PercentOfQuantity
        public DamageSpec(QuantityKind quantity, StatScope owner, float coefficient,
                          DamageTypeSO type, bool? affectedByChainFalloff = null)
        {
            Kind = DerivationKind.PercentOfQuantity;
            Coefficient = coefficient;
            Type = type;
            Owner = owner;
            Stat = null;
            Quantity = quantity;
            HasOverride = affectedByChainFalloff.HasValue;
            AffectedByChainFalloffOverride = affectedByChainFalloff ?? false;
        }

        // Source-anchored derivations fall off with chain distance; target-anchored
        // do not (a %-of-target-HP hit shouldn't shrink because it's a far chain link).
        // Default by scope: Target -> not affected; Attacker/Source/Flat -> affected.
        public bool AffectedByChainFalloff
        {
            get
            {
                if (HasOverride) return AffectedByChainFalloffOverride;
                if (Kind == DerivationKind.Flat) return true;
                return Owner != StatScope.Target;
            }
        }

        // Resolve the derivation to a raw number (before chain falloff / precision /
        // crit / defense — those are applied by the effect).
        public float Resolve(in DerivationContext ctx)
        {
            switch (Kind)
            {
                case DerivationKind.Flat:
                    return Coefficient;
                case DerivationKind.PercentOfStat:
                    return Coefficient * ResolveStat(in ctx);
                case DerivationKind.PercentOfQuantity:
                    return Coefficient * ResolveQuantity(in ctx);
                default:
                    return 0f;
            }
        }

        private float ResolveStat(in DerivationContext ctx)
        {
            if (Stat == null) return 0f;
            StatContainer container = OwnerContainer(in ctx);
            if (container == null) return 0f;
            return container.Resolve(Stat);
        }

        private StatContainer OwnerContainer(in DerivationContext ctx)
        {
            switch (Owner)
            {
                case StatScope.Attacker: return ctx.Attacker != null ? ctx.Attacker.Stats : null;
                case StatScope.Source: return ctx.Source != null ? ctx.Source.SourceStats : null;
                case StatScope.Target: return ctx.Target != null ? ctx.Target.Stats : null;
                default: return null;
            }
        }

        private float ResolveQuantity(in DerivationContext ctx)
        {
            // quantities read HEALTH — Source has no health.
            ICombatant c;
            switch (Owner)
            {
                case StatScope.Attacker: c = ctx.Attacker; break;
                case StatScope.Target: c = ctx.Target; break;
                case StatScope.Source:
                    WarnInvalidSourceQuantity();
                    return 0f;
                default: return 0f;
            }
            if (c == null) return 0f;

            switch (Quantity)
            {
                case QuantityKind.CurrentHealth: return c.CurrentHealth;
                case QuantityKind.MissingHealth: return Mathf.Max(0f, c.MaxHealth - c.CurrentHealth);
                case QuantityKind.HealthFraction:
                    return c.MaxHealth > 0f ? c.CurrentHealth / c.MaxHealth : 0f;
                default: return 0f;
            }
        }

        // warn once per session about the invalid Source+quantity combo
        private static bool warnedSourceQuantity;
        private static void WarnInvalidSourceQuantity()
        {
            if (warnedSourceQuantity) return;
            warnedSourceQuantity = true;
            Debug.LogWarning("[DamageSpec] PercentOfQuantity with Source scope is invalid " +
                             "(a source has no health). Resolving to 0. Use Attacker or Target.");
        }
    }
}