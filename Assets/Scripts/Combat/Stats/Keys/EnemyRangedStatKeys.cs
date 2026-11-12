using UnityEngine;

namespace Combat.Stats
{
    // Ranged attack stat definitions an enemy resolves. Prefixed ranged_ so none clash with weapon stats.
    [CreateAssetMenu(fileName = "EnemyRangedStatKeys", menuName = "Combat/Stats/Enemy Ranged Stat Keys")]
    public class EnemyRangedStatKeys : ScriptableObject
    {
        public StatDefinitionSO damage;           // "ranged_damage"
        public StatDefinitionSO fireRate;         // "ranged_fire_rate" (shots per minute in a burst)
        public StatDefinitionSO burstSize;        // "ranged_burst_size"
        public StatDefinitionSO burstSeparation;  // "ranged_burst_separation" (s)
        public StatDefinitionSO aimError;         // "ranged_aim_error" (deg, first engaging)
        public StatDefinitionSO aimErrorMin;      // "ranged_aim_error_min" (deg, settled)
        public StatDefinitionSO aimSettleTime;    // "ranged_aim_settle_time" (s)
        public StatDefinitionSO burstSpread;      // "ranged_burst_spread" (deg, per shot in a burst)
        public StatDefinitionSO targetDelay;      // "ranged_target_delay" (s)
        public StatDefinitionSO tracking;         // "ranged_tracking" (0-1)
        public StatDefinitionSO leading;          // "ranged_leading" (0-1)
        public StatDefinitionSO projectileSpeed;  // "ranged_projectile_speed" (multiplier)
        public StatDefinitionSO maxRange;         // "ranged_max_range" (m)
        public StatDefinitionSO magazine;         // "ranged_magazine" (0 = never reloads)
        public StatDefinitionSO reloadTime;       // "ranged_reload_time" (s)
        public StatDefinitionSO windup;           // "ranged_windup" (s)
    }
}
