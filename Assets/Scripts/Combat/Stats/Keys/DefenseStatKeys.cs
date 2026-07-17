using UnityEngine;
using Combat.Stats;

namespace Combat.Stats
{
    // References the general defensive stats the health component resolves.
    // Per-TYPE resistance stats are referenced on their DamageTypeSO
    // (DamageTypeSO.resistanceStat), not here. This holds the type-agnostic ones.
    [CreateAssetMenu(fileName = "DefenseStatKeys", menuName = "Combat/Stats/Defense Stat Keys")]
    public class DefenseStatKeys : ScriptableObject
    {
        [Tooltip("General incoming-damage amplification (vulnerability). Pool, base 0. " +
                 "+0.3 = takes 30% more; debuffs modify this. Drives WasDebuffed.")]
        public StatDefinitionSO damageTaken;   // "damage_taken"
    }
}
