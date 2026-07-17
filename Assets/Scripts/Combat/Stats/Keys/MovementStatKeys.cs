using UnityEngine;
using Combat.Stats;

namespace Combat.Stats
{
    // References the movement stat definitions PlayerMovement resolves. Assign once
    // on the movement component. Same pattern as WeaponStatKeys / CombatStatKeys.
    [CreateAssetMenu(fileName = "MovementStatKeys", menuName = "Combat/Stats/Movement Stat Keys")]
    public class MovementStatKeys : ScriptableObject
    {
        public StatDefinitionSO moveSpeed;           // "move_speed"
        public StatDefinitionSO sprintMultiplier;    // "sprint_multiplier"
        public StatDefinitionSO jumpForce;           // "jump_force"
        public StatDefinitionSO groundAcceleration;  // "ground_acceleration"
        public StatDefinitionSO airAcceleration;     // "air_acceleration"
        public StatDefinitionSO gravity;             // "gravity"
    }
}
