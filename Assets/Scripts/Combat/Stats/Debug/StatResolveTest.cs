using System.Collections.Generic;
using UnityEngine;

namespace Combat.Stats
{
    // Phase 2b verification: prove the bucket math for both resolution modes.
    // Assign weapon_damage (ScaledBase) and crit_damage (Pool, base/default 0.5).
    // Delete once verified.
    public class StatResolveTest : MonoBehaviour
    {
        [SerializeField] private StatDefinitionSO weaponDamage; // ScaledBase
        [SerializeField] private StatDefinitionSO critDamage;   // Pool, inherent 0.5

        private void Start()
        {
            // Test 1: ScaledBase — base 10, +50% additive, ×1.2 => 18
            var mods1 = new List<StatModifierValue>
            {
                new StatModifierValue(StatResolver.ADDITIVE, 0.5f),
                new StatModifierValue(StatResolver.MULTIPLICATIVE, 0.2f),
            };
            Debug.Log($"[ResolveTest] weapon_damage 10 +50% ×1.2 = {StatResolver.Resolve(weaponDamage, 10f, mods1)} (expect 18)");

            // Test 2: ScaledBase — (10+5 flat) ×1.5 ×1.2 => 27
            var mods2 = new List<StatModifierValue>
            {
                new StatModifierValue(StatResolver.FLAT, 5f),
                new StatModifierValue(StatResolver.ADDITIVE, 0.5f),
                new StatModifierValue(StatResolver.MULTIPLICATIVE, 0.2f),
            };
            Debug.Log($"[ResolveTest] weapon_damage (10+5) ×1.5 ×1.2 = {StatResolver.Resolve(weaponDamage, 10f, mods2)} (expect 27)");

            // Test 3: Pool (crit_damage) — inherent base 0.5 IN the additive pool,
            // + items 4.101 additive, × 1.445 mult => (0.5 + 4.101) × 1.445 ≈ 6.65
            var mods3 = new List<StatModifierValue>
            {
                new StatModifierValue(StatResolver.ADDITIVE, 4.101f),
                new StatModifierValue(StatResolver.MULTIPLICATIVE, 0.445f),
            };
            Debug.Log($"[ResolveTest] crit_damage Pool (0.5 inherent + 4.101) ×1.445 = {StatResolver.Resolve(critDamage, critDamage.defaultValue, mods3)} (expect ~6.65)");

            // Test 4: floor — ScaledBase base 10 with -150% additive => 0
            var mods4 = new List<StatModifierValue>
            {
                new StatModifierValue(StatResolver.ADDITIVE, -1.5f),
            };
            Debug.Log($"[ResolveTest] weapon_damage 10 with -150% additive = {StatResolver.Resolve(weaponDamage, 10f, mods4)} (expect 0, floored)");
        }
    }
}