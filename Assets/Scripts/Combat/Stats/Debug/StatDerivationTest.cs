using UnityEngine;

namespace Combat.Stats
{
    // Phase 2d verification: derived modifiers, cross-stat invalidation, cycle guard.
    // Assign crit_damage (Pool) and global_damage (Pool). Delete once verified.
    public class StatDerivationTest : MonoBehaviour
    {
        [SerializeField] private StatDefinitionSO critDamage;    // Pool
        [SerializeField] private StatDefinitionSO globalDamage;  // Pool, we drive this

        private void Start()
        {
            var c = new StatContainer();

            // global_damage: base 0 (Pool), set a base additive to give it a value.
            // We'll SetBase to simulate "you have +2.0 global damage bonus".
            c.SetBase(globalDamage, 2.0f);
            Debug.Log($"[DerivTest] global_damage = {c.Resolve(globalDamage)} (expect 2)");

            // crit_damage: inherent 0.5 (Pool base/default), PLUS a DERIVED modifier:
            // "+20% of your global_damage bonus, up to 0.4" (the D4 Earthen pattern).
            // global_damage is 2.0 -> 0.2 × 2.0 = 0.4, capped at 0.4 -> +0.4 additive.
            // crit_damage Pool = (0.5 inherent + 0.4 derived) = 0.9
            var derived = new StatModifier(
                critDamage, StatResolver.ADDITIVE,
                sourceStat: globalDamage, coefficient: 0.2f, useCap: true, cap: 0.4f);
            c.AddModifier(derived, owner: this);
            Debug.Log($"[DerivTest] crit_damage (0.5 + derived 0.4 capped) = {c.Resolve(critDamage)} (expect 0.9)");

            // Now CHANGE global_damage -> crit_damage's derived contribution must
            // update (cross-stat invalidation). Lower global to 1.0 -> 0.2×1.0=0.2
            // (under cap) -> crit_damage = 0.5 + 0.2 = 0.7
            c.SetBase(globalDamage, 1.0f);
            Debug.Log($"[DerivTest] after global->1.0, crit_damage = {c.Resolve(critDamage)} (expect 0.7)");

            // Raise global high so the cap bites: global 5.0 -> 0.2×5=1.0 capped to 0.4
            // crit_damage = 0.5 + 0.4 = 0.9
            c.SetBase(globalDamage, 5.0f);
            Debug.Log($"[DerivTest] after global->5.0 (cap bites), crit_damage = {c.Resolve(critDamage)} (expect 0.9)");

            // Cache behavior: resolve again without changing anything -> cached, same.
            Debug.Log($"[DerivTest] crit_damage cached = {c.Resolve(critDamage)} (expect 0.9, cached)");

            // --- Cycle guard: make crit_damage derive from global_damage AND
            // global_damage derive from crit_damage. Resolving should NOT loop. ---
            var cycle = new StatModifier(
                globalDamage, StatResolver.ADDITIVE,
                sourceStat: critDamage, coefficient: 0.1f);
            c.AddModifier(cycle, owner: this);
            Debug.Log($"[DerivTest] CYCLE: resolving crit_damage should warn once and not hang...");
            float cyc = c.Resolve(critDamage);
            Debug.Log($"[DerivTest] crit_damage with cycle present = {cyc} (finite, no hang/crash)");
        }
    }
}
