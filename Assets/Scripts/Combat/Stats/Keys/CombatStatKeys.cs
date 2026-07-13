using UnityEngine;
using Combat.Stats;

namespace Combat.Core
{
    // References the player-scope stat definitions the RESOLVER reads at hit time.
    // Assign the asset on the WeaponHitResolver. Same pattern as WeaponStatKeys:
    // name stats by asset, resolve by index at runtime.
    [CreateAssetMenu(fileName = "CombatStatKeys", menuName = "Combat/Stats/Combat Stat Keys")]
    public class CombatStatKeys : ScriptableObject
    {
        [Tooltip("id \"crit_chance\" — Pool mode, 0..1 clamp recommended.")]
        public StatDefinitionSO critChance;

        [Tooltip("id \"crit_damage\" — Pool mode, inherent base 0.5 (+50%).")]
        public StatDefinitionSO critDamage;

        // global_damage and other player-scope stats can be added here as they're consumed.
    }
}
