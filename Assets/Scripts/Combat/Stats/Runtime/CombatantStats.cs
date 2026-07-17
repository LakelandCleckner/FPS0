using UnityEngine;
using Combat.Core;

namespace Combat.Stats
{
    // The combatant identity + its stat container. Implements ICombatant: stats are
    // read directly from here (crit, global, etc. — NO health coupling); health,
    // defense, faction, and TakeDamage are delegated to a CombatantHealth sibling.
    //
    // Because it also passes through TakeDamage, a hitbox/delivery needs only ONE
    // reference (the CombatantStats/ICombatant) for both the hit context AND dealing
    // damage.
    public class CombatantStats : MonoBehaviour, ICombatant
    {
        [System.Serializable]
        public struct BaseStat
        {
            public StatDefinitionSO stat;
            public float baseValue;
        }

        [Tooltip("Inherent base values (crit_chance, crit_damage, max_health, ...). " +
                 "Modifiers layer on top.")]
        [SerializeField] private BaseStat[] baseStats;

        [Header("Health (optional sibling)")]
        [Tooltip("Health component this combatant delegates to. Auto-found if empty.")]
        [SerializeField] private CombatantHealth health;

        [Header("Identity")]
        [SerializeField] private int faction = 1;

        private StatContainer container;
        public StatContainer Container => container;

        // ---- ICombatant ----
        public StatContainer Stats => container;              // direct — no health path
        public int Faction => faction;

        public float MaxHealth => health != null ? health.MaxHealth : 0f;
        public float CurrentHealth => health != null ? health.CurrentHealth : 0f;
        public bool IsDying => health != null && health.IsDying;
        public bool IsDebuffed => health != null && health.IsDebuffed;
        public float GetDamageMultiplier(DamageTypeSO type, BodyPart bodyPart)
            => health != null ? health.GetDamageMultiplier(type, bodyPart) : 1f;

        // Damage passthrough -> the health sibling actually mutates health. Lets a
        // hitbox deal damage through the same reference it uses as the ICombatant.
        public void TakeDamage(float damage, BodyPart partHit, DamageTypeSO type)
            => health?.TakeDamage(damage, partHit, type);

        public void Heal(float amount) => health?.Heal(amount);

        private void Awake()
        {
            container = new StatContainer();
            if (baseStats != null)
                foreach (var b in baseStats)
                    if (b.stat != null)
                        container.SetBase(b.stat, b.baseValue);

            if (health == null)
                health = GetComponent<CombatantHealth>();
        }

        public float Resolve(StatDefinitionSO stat)
            => container != null ? container.Resolve(stat) : (stat != null ? stat.defaultValue : 0f);
    }
}