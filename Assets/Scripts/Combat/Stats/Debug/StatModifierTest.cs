using UnityEngine;

namespace Combat.Stats
{
    // Phase 2c verification: registration, resolution, caching, precise + bulk
    // removal, stacking. Assign weapon_damage (ScaledBase) and crit_damage (Pool).
    // Delete once verified.
    public class StatModifierTest : MonoBehaviour
    {
        [SerializeField] private StatDefinitionSO weaponDamage; // ScaledBase, base set to 10
        [SerializeField] private StatDefinitionSO critDamage;   // Pool, inherent 0.5

        private void Start()
        {
            var c = new StatContainer();
            c.SetBase(weaponDamage, 10f);

            // baseline: no modifiers -> 10
            Debug.Log($"[ModTest] weapon_damage base = {c.Resolve(weaponDamage)} (expect 10)");

            // add +50% additive -> 15
            var add50 = new StatModifier(weaponDamage, StatResolver.ADDITIVE, 0.5f);
            var hAdd50 = c.AddModifier(add50, owner: this);
            Debug.Log($"[ModTest] +50% additive = {c.Resolve(weaponDamage)} (expect 15)");

            // resolve again -> cache hit, same value (no recompute)
            Debug.Log($"[ModTest] cache hit = {c.Resolve(weaponDamage)} (expect 15, cached)");

            // add ×1.2 mult -> 15 * 1.2 = 18
            var mult20 = new StatModifier(weaponDamage, StatResolver.MULTIPLICATIVE, 0.2f);
            var hMult = c.AddModifier(mult20, owner: this);
            Debug.Log($"[ModTest] +×1.2 mult = {c.Resolve(weaponDamage)} (expect 18)");

            // stacking: another +50% additive from a DIFFERENT owner -> (1 + 0.5 + 0.5)=2.0
            // 10 * 2.0 * 1.2 = 24
            var otherOwner = new object();
            var add50b = new StatModifier(weaponDamage, StatResolver.ADDITIVE, 0.5f);
            var hAdd50b = c.AddModifier(add50b, owner: otherOwner);
            Debug.Log($"[ModTest] +another 50% (stacks) = {c.Resolve(weaponDamage)} (expect 24)");

            // precise removal: remove the first +50% by handle -> back to (1+0.5)=1.5
            // 10 * 1.5 * 1.2 = 18
            c.RemoveModifier(hAdd50);
            Debug.Log($"[ModTest] removed first 50% by handle = {c.Resolve(weaponDamage)} (expect 18)");

            // bulk removal: remove everything owned by `this` -> removes mult20 (and
            // add50 already gone). Leaves add50b (otherOwner). -> 10 * 1.5 = 15
            int removed = c.RemoveAllFromOwner(this);
            Debug.Log($"[ModTest] RemoveAllFromOwner(this) removed {removed}; result = {c.Resolve(weaponDamage)} (expect 15)");

            // idempotent: removing an already-removed handle is a safe no-op
            bool again = c.RemoveModifier(hAdd50);
            Debug.Log($"[ModTest] remove already-gone handle returned {again} (expect False, no error)");

            // --- crit_damage (Pool) with a modifier ---
            var critC = new StatContainer();
            // inherent 0.5 comes from base/default; add +4.101 additive items
            var critItems = new StatModifier(critDamage, StatResolver.ADDITIVE, 4.101f);
            critC.AddModifier(critItems, owner: this);
            var critMult = new StatModifier(critDamage, StatResolver.MULTIPLICATIVE, 0.445f);
            critC.AddModifier(critMult, owner: this);
            Debug.Log($"[ModTest] crit_damage Pool (0.5 + 4.101) ×1.445 = {critC.Resolve(critDamage)} (expect ~6.65)");
        }
    }
}
