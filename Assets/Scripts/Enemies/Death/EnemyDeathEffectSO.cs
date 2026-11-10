using UnityEngine;
using Combat.Core;
using Combat.Stats;

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

        // Everything read off the dead enemy, taken at the moment of death.
        public Resolved Resolve(in DeathEffectContext ctx)
        {
            float k = coefficient * ctx.TierScale(tierScaleStat);
            bool onTarget = stat != null && basis == DeathDamageBasis.TargetStat;
            if (!onTarget && stat != null) k = ctx.Scaled(stat, k);
            return new Resolved(onTarget ? stat : null, k, type);
        }

        public readonly struct Resolved
        {
            private readonly StatDefinitionSO targetStat;
            private readonly float amount;
            private readonly DamageTypeSO type;

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
    public abstract class EnemyDeathEffectSO : ScriptableObject
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

        public int PlayerMask => hitsPlayer ? playerLayers.value : 0;
        public int EnemyMask => hitsEnemies ? enemyLayers.value : 0;

        public abstract void Trigger(in DeathEffectContext ctx);
    }
}
