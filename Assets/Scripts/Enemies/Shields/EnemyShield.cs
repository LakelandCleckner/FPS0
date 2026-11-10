using UnityEngine;
using Combat.Core;
using Combat.Stats;

namespace Combat.Enemies
{
    // Live shield on an enemy. Soaks damage before health, overflows on break, refills after a pause.
    // Added and configured by EnemyDefinitionApplier when a layer grants a shield.
    public class EnemyShield : MonoBehaviour, IShieldSource, IDamageAbsorber
    {
        private EnemyShieldSO def;
        private CombatantHealth health;
        private CombatantStats owner;
        private WeaponHitResolver resolver;
        private float lastHit;

        public float CurrentShield { get; private set; }
        public float MaxShield { get; private set; }
        public Color ShieldColor => def != null ? def.Color : Color.clear;
        public bool Up => def != null && CurrentShield > 0f;

        // Null turns the shield off.
        public void Configure(EnemyShieldSO so, CombatantHealth h, CombatantStats stats, WeaponHitResolver hitResolver)
        {
            def = so;
            health = h;
            owner = stats;
            resolver = hitResolver;
            MaxShield = so != null && h != null ? h.MaxHealth * so.percentOfMaxHealth : 0f;
            CurrentShield = MaxShield;
            lastHit = -999f;
            enabled = so != null;
        }

        // Returns what passes through to health.
        public float Absorb(float damage, DamageTypeSO type)
        {
            if (!Up || damage <= 0f) return damage;

            bool match = type != null && type == def.element;
            float mult = match ? def.matchingMultiplier : def.otherMultiplier;
            if (mult <= 0f) { lastHit = Time.time; return 0f; }

            float shieldDamage = damage * mult;
            lastHit = Time.time;
            if (shieldDamage < CurrentShield)
            {
                CurrentShield -= shieldDamage;
                return 0f;
            }

            // Break: the leftover goes to health at its normal rate.
            float overflow = (shieldDamage - CurrentShield) / mult;
            CurrentShield = 0f;
            Break(match);
            return overflow;
        }

        private void Break(bool match)
        {
            if (def.breakEffect == null || (def.breakEffectOnlyOnMatch && !match)) return;
            var at = transform.position;
            def.breakEffect.Trigger(new DeathEffectContext(at, owner, resolver));
        }

        private void Update()
        {
            if (def == null || def.rechargeDelay <= 0f || CurrentShield >= MaxShield) return;
            if (health != null && health.IsDying) return;
            if (Time.time - lastHit < def.rechargeDelay) return;
            CurrentShield = Mathf.Min(MaxShield, CurrentShield + MaxShield / def.rechargeTime * Time.deltaTime);
        }
    }
}
