using UnityEngine;

namespace Combat.Stats
{
    [CreateAssetMenu(fileName = "EnemyAttackStatKeys", menuName = "Combat/Stats/Enemy Attack Stat Keys")]
    public class EnemyAttackStatKeys : ScriptableObject
    {
        public StatDefinitionSO attackDamage;   // "attack_damage"
        public StatDefinitionSO attackRange;    // "attack_range" (metres)
        public StatDefinitionSO attackCooldown; // "attack_cooldown" (seconds after a swing ends)
        public StatDefinitionSO attackSpeed;    // "attack_speed" (1 = clip speed)
    }
}
