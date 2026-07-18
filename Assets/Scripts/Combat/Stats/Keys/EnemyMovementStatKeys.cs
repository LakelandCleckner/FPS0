using UnityEngine;

namespace Combat.Stats
{
    // Movement stat definitions an AI agent resolves. move_speed is the SAME stat
    // definition the player uses — a slow modifier works on anything that has it.
    [CreateAssetMenu(fileName = "EnemyMovementStatKeys", menuName = "Combat/Stats/Enemy Movement Stat Keys")]
    public class EnemyMovementStatKeys : ScriptableObject
    {
        [Tooltip("Shared with the player: the same move_speed StatDefinitionSO.")]
        public StatDefinitionSO moveSpeed;                  // "move_speed"
        public StatDefinitionSO chaseSpeedMultiplier;       // "chase_speed_multiplier"
        public StatDefinitionSO investigateSpeedMultiplier; // "investigate_speed_multiplier"
    }
}
