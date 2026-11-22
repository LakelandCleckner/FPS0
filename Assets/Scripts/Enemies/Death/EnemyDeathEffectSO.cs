using UnityEngine;
using Combat.Core;
using Combat.Stats;
using Combat.Status;
using Combat.Info;

namespace Combat.Enemies
{
    // What a dying enemy hands its death effects.
    public readonly struct DeathEffectContext
    {
        public readonly Vector3 Position;
        public readonly CombatantStats Owner;
        public readonly WeaponHitResolver Resolver;

        public DeathEffectContext(Vector3 position, CombatantStats owner, WeaponHitResolver resolver)
        {
            Position = position;
            Owner = owner;
            Resolver = resolver;
        }

        public int Faction => Owner != null ? Owner.Faction : 0;

        // Owner stat x coefficient, read at the moment of death.
        public float Scaled(StatDefinitionSO stat, float coefficient)
        {
            var c = Owner != null ? Owner.Container : null;
            float v = stat != null && c != null ? c.Resolve(stat) : 1f;
            return v * coefficient;
        }

        // How much modifiers raised a stat over its base (Major attack_damage = 1.25). 1 when unset.
        public float TierScale(StatDefinitionSO stat)
        {
            var c = Owner != null ? Owner.Container : null;
            if (stat == null || c == null) return 1f;
            float b = c.GetBase(stat);
            return b > 0f ? c.Resolve(stat) / b : 1f;
        }
    }

    public enum DeathDamageBasis
    {
        TargetStat,     // stat on whoever is hit (max_health = % of their max health)
        DeadEnemyStat   // stat on the enemy that died
    }

    // Damage a death effect deals per hit.
    [System.Serializable]
    public class DeathDamage
    {
        public DeathDamageBasis basis = DeathDamageBasis.TargetStat;
        [Tooltip("Empty = coefficient is flat damage.")]
        public StatDefinitionSO stat;
        [Tooltip("0.15 with max_health = 15% of the target's max health.")]
        public float coefficient = 0.15f;
        [Tooltip("Scales by how much tiers raised this stat on the dead enemy (attack_damage: Major x1.25). Empty = no scaling.")]
        public StatDefinitionSO tierScaleStat;
        [Tooltip("Also sets the effect's colour.")]
        public DamageTypeSO type;

        public bool Any => coefficient > 0f;

        // "15% of the target's max health as Fire, higher on stronger ranks".
        public void Describe(Describer d, string token = "damage", bool enemyElement = false)
        {
            if (stat == null) d.Num(Describer.Trim(coefficient), token).Add(" damage");
            else d.Percent(coefficient, token).Add(basis == DeathDamageBasis.TargetStat ? " of the target's " : " of its own ")
                  .Add(Describer.StatName(stat).ToLowerInvariant());
            if (enemyElement) d.Add(" of its element");
            else if (type != null) d.Add(" as ").Element(type);
            if (tierScaleStat != null) d.Add(", higher on stronger ranks");
        }

        // Everything read off the dead enemy, taken at the moment of death. Element replaces the authored type.
        public Resolved Resolve(in DeathEffectContext ctx, DamageTypeSO element = null)
        {
            float k = coefficient * ctx.TierScale(tierScaleStat);
            bool onTarget = stat != null && basis == DeathDamageBasis.TargetStat;
            if (!onTarget && stat != null) k = ctx.Scaled(stat, k);
            return new Resolved(onTarget ? stat : null, k, element != null ? element : type);
        }

        public readonly struct Resolved
        {
            private readonly StatDefinitionSO targetStat;
            private readonly float amount;
            private readonly DamageTypeSO type;

            public DamageTypeSO Type => type;

            public Resolved(StatDefinitionSO targetStat, float amount, DamageTypeSO type)
            {
                this.targetStat = targetStat;
                this.amount = amount;
                this.type = type;
            }

            public DamageSpec Spec(float scale = 1f)
                => targetStat != null
                    ? new DamageSpec(targetStat, StatScope.Target, amount * scale, type)
                    : new DamageSpec(amount * scale, type);
        }
    }

    // Something an enemy leaves behind when it dies: a blast, a hazard pool.
    public abstract class EnemyDeathEffectSO : ScriptableObject, IDescribable
    {
        [Header("Targets")]
        public bool hitsPlayer = true;
        [Tooltip("Also hurts other enemies, so deaths can chain.")]
        public bool hitsEnemies = false;
        public LayerMask playerLayers = 1 << 8;
        public LayerMask enemyLayers = 1 << 7;

        [Header("Counterplay")]
        [Tooltip("A precision (head) kill stops this effect.")]
        public bool cancelOnPrecisionKill = false;

        [Header("Feedback")]
        [Tooltip("Floating damage numbers on enemies this hurts.")]
        public bool showDamageNumbers = true;

        [Header("Element")]
        [Tooltip("Damage type, colour and status from the dying enemy's Element layer. Falls back to this effect's own type.")]
        public bool useEnemyElement = false;

        public int PlayerMask => hitsPlayer ? playerLayers.value : 0;
        public int EnemyMask => hitsEnemies ? enemyLayers.value : 0;

        public abstract void Trigger(in DeathEffectContext ctx);

        // The enemy's element when Use Enemy Element is on and it has one, else the authored type.
        protected DamageTypeSO ElementFor(in DeathEffectContext ctx, DamageTypeSO own)
        {
            if (!useEnemyElement || ctx.Owner == null) return own;
            var applier = ctx.Owner.GetComponentInParent<EnemyDefinitionApplier>();
            var e = applier != null ? applier.Element : null;
            return e != null ? e : own;
        }

        // The authored status, else the element's own status when Use Enemy Element is on.
        protected StatusSO StatusFor(DamageTypeSO element, StatusSO own)
            => own != null ? own : useEnemyElement && element != null ? element.status : null;

        public void Describe(Describer d) => DescribeAs(d, "On death: ");

        // Lead is the condition, e.g. "On death: " or "When broken: ".
        public virtual void DescribeAs(Describer d, string lead) { }

        // Who it hurts and whether a precision kill stops it.
        protected void DescribeTargets(Describer d)
        {
            if (hitsPlayer && hitsEnemies) d.Next().Add("Hurts you and other enemies.");
            else if (hitsEnemies) d.Next().Add("Hurts other enemies.");
            if (cancelOnPrecisionKill) d.Next().Add("A ").Keyword("precision", "precision").Add(" kill stops it.");
        }
    }
}
