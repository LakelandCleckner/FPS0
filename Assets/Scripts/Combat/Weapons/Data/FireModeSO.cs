using UnityEngine;

namespace Combat.Weapons
{
    // A FIRE MODE = one firing behavior + one delivery, composed. The two axes are
    // fully decoupled; this just pairs a chosen behavior with a chosen delivery.
    // Any behavior works with any delivery (auto+hitscan, charge+projectile, ...).
    // A weapon's archetype holds its primary fire mode; alt-fire = another mode
    // (later).
    [CreateAssetMenu(fileName = "FireMode", menuName = "Combat/Weapons/Fire Mode")]
    public class FireModeSO : ScriptableObject
    {
        public string displayName = "";

        [Tooltip("WHEN shots happen (semi/auto/burst/charge).")]
        public FireBehaviorSO fireBehavior;

        [Tooltip("HOW the shot reaches the target (hitscan/projectile).")]
        public DeliverySO delivery;
    }
}
