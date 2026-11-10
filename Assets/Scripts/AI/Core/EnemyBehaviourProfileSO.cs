using UnityEngine;

namespace Combat.AI
{
    // How an enemy fights: which GOAP capabilities its agent type gets, plus their tuning.
    // Archetypes set a default; modifiers can override it (last layer that sets one wins).
    [CreateAssetMenu(fileName = "BehaviourProfile", menuName = "Combat/Enemies/Behaviour Profile")]
    public class EnemyBehaviourProfileSO : ScriptableObject
    {
        [Header("Capabilities")]
        [Tooltip("Close in and swing. Needs an attack token.")]
        public bool melee = true;
        [Tooltip("Sprint at the target and self-detonate in range. Ignores attack tokens.")]
        public bool kamikaze = false;
        [Tooltip("When its shield breaks, take cover until it recharges.")]
        public bool shieldCover = false;
        [Tooltip("Search the last known position after losing the target.")]
        public bool search = true;
        [Tooltip("Wander when there's nothing to do.")]
        public bool idle = true;

        [Header("Melee")]
        [Tooltip("Distance kept from the target while waiting for an attack token.")]
        public float holdDistance = 5f;
        [Tooltip("Degrees per second spent circling while waiting for a token.")]
        public float holdCircleSpeed = 12f;

        [Header("Kamikaze")]
        public float kamikazeTriggerRange = 2.5f;
        [Tooltip("Speed factor on move_speed while rushing.")]
        public float kamikazeSpeedFactor = 2.5f;

        [Header("Cover")]
        [Tooltip("How far from itself the enemy looks for cover.")]
        public float coverSearchRadius = 15f;
        [Tooltip("Cover closer than this to the target is ignored.")]
        public float coverMinTargetDistance = 6f;
        [Tooltip("With no cover found, back away this far from the target instead.")]
        public float retreatDistance = 10f;

        // Agent type id for this profile.
        public string AgentTypeId => "Enemy_" + name;
    }
}
