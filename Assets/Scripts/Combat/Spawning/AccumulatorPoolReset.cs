using UnityEngine;
using Combat.Core;
using Combat.Feedback;
using Combat.Spawning;

namespace Combat.Feedback
{
    // Two jobs, both about the damage-number accumulator surviving pooled reuse:
    //
    // 1. Clears this enemy's accumulator numbers on despawn/spawn.
    // 2. Owns a per-LIFE generation counter. A pooled enemy is the SAME ICombatant
    //    across lives, which is exactly why numbers bleed between lives — clearing
    //    can always lose a same-frame race (a status tick that reports damage on the
    //    very frame the enemy dies). The generation makes it correct by construction:
    //    each accumulator number remembers the generation it was born under, and the
    //    registry drops any number whose generation no longer matches the target's
    //    current one. A number from a previous life therefore can never render on the
    //    reused enemy, regardless of frame timing. Same guard pattern as
    //    HitContext.Generation.
    public class AccumulatorPoolReset : MonoBehaviour, IPoolable
    {
        private ICombatant combatant;

        // Bumped every spawn. The registry reads this off the target to tell whether a
        // number belongs to the current life.
        public int Generation { get; private set; }

        private void Awake()
        {
            combatant = GetComponent<ICombatant>();
        }

        public void OnSpawn()
        {
            if (combatant == null) combatant = GetComponent<ICombatant>();

            // New life: bump first so any number created from here on is stamped with
            // the new generation, then clear anything left keyed to this combatant.
            Generation++;

            if (combatant != null && DamageAccumulatorRegistry.Instance != null)
                DamageAccumulatorRegistry.Instance.ClearTarget(combatant);
        }

        public void OnDespawn()
        {
            if (combatant == null) combatant = GetComponent<ICombatant>();
            if (combatant != null && DamageAccumulatorRegistry.Instance != null)
                DamageAccumulatorRegistry.Instance.ClearTarget(combatant);
        }

        // Registry helper: read the current generation for a target, or 0 if the target
        // isn't a pooled enemy (non-pooled things never reuse, so a fixed 0 is fine —
        // their numbers never mismatch).
        public static int GetGeneration(ICombatant target)
        {
            var mb = target as MonoBehaviour;
            if (mb == null) return 0;
            var reset = mb.GetComponent<AccumulatorPoolReset>();
            return reset != null ? reset.Generation : 0;
        }
    }
}