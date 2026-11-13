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
        [Tooltip("Hold a firing position and shoot with EnemyRangedAttack.")]
        public bool ranged = false;
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

        [Header("Ranged: range band")]
        [Tooltip("Closer than this: melee if it has melee, otherwise back off. Max range comes from ranged_max_range.")]
        public float rangedMinRange = 5f;
        [Tooltip("Preferred distance at Normal defensiveness.")]
        public float rangedPreferredRange = 14f;
        [Tooltip("Preferred distance shift toward min (Aggressive) or max (Defensive), as a fraction of the band.")]
        [Range(0f, 1f)] public float rangedBandShift = 0.35f;

        [Header("Ranged: firing positions")]
        [Tooltip("How far from itself the enemy looks for firing points.")]
        public float firingPointSearchRadius = 14f;
        [Tooltip("Seconds between firing point checks.")]
        public float firingPointRecheck = 0.75f;
        [Tooltip("Counts as at the firing point within this distance (m).")]
        public float firingPointTolerance = 1.25f;
        [Tooltip("Other enemies within this distance of a point count against it (m).")]
        public float allySpacing = 2.5f;
        public PointWeights fireWeights = PointWeights.Fire;
        [Tooltip("Speed for short repositions: a combat walk with the rifle up (m/s).")]
        public float walkSpeed = 1.5f;
        [Tooltip("Speed for long moves and advances (m/s).")]
        public float runSpeed = 3.5f;
        [Tooltip("Moves longer than this run, shorter ones walk (m).")]
        public float runDistance = 6f;
        [Tooltip("Face a visible target while walking (strafe). Running always faces the way it's going.")]
        public bool faceWhileMoving = true;
        [Tooltip("Keep firing bursts while walking between firing points with the target in sight. Running never fires.")]
        public bool fireWhileWalking = true;
        [Tooltip("Seconds held at a firing point before looking for a better one (random between min and max). A point that breaks is left at once.")]
        public float pointHoldMin = 2f;
        public float pointHoldMax = 4f;

        [Header("Ranged: cover")]
        [Tooltip("Fight from cover when there is some in the range band: hide, peek out, burst, duck back. Falls back to firing points.")]
        public bool useCover = true;
        public PointWeights coverWeights = PointWeights.Cover;
        [Tooltip("Seconds held at a cover spot before looking for a better one (random between min and max). Cover that stops hiding is left at once.")]
        public float coverHoldMin = 8f;
        public float coverHoldMax = 15f;
        [Tooltip("Seconds hidden between peeks (random between min and max).")]
        public float hideMin = 0.8f;
        public float hideMax = 2f;
        [Tooltip("Bursts fired per peek before ducking back.")]
        [Min(1)] public int peekBursts = 1;
        [Tooltip("Longest time out of cover per peek (s).")]
        public float peekMaxTime = 3f;

        [Header("Ranged: firing feel")]
        [Tooltip("Aim cone growth per m/s of target speed (degrees).")]
        public float aimErrorPerTargetSpeed = 0.6f;
        [Tooltip("Random spread on burst separation (0 = exact, 0.5 = +-50%). 0 lets a group fire together.")]
        [Range(0f, 1f)] public float burstSeparationJitter = 0f;
        [Tooltip("Damage within the flinch window, as a fraction of max health, that cuts a burst.")]
        public float flinchDamageFraction = 0.1f;
        [Tooltip("Seconds before firing again after a flinch.")]
        public float flinchRecovery = 0.6f;
        [Tooltip("Seconds without sight of the target before the weapon is lowered (raising again plays the wind-up).")]
        public float lowerAfter = 2.5f;

        [Header("Defensiveness")]
        [Tooltip("Added per fraction of max health lost.")]
        public float defDamage = 1.5f;
        [Tooltip("Added per second while the target aims at this enemy.")]
        public float defAimedAt = 0.25f;
        [Tooltip("Added per ally death within Ally Death Radius.")]
        public float defAllyDeath = 0.2f;
        public float defAllyDeathRadius = 12f;
        [Tooltip("Added per second while the target is inside the min range.")]
        public float defProximity = 0.3f;
        [Tooltip("Added when hit from beyond ranged_max_range.")]
        public float defOutranged = 0.35f;
        [Tooltip("Removed per second.")]
        public float defDecay = 0.1f;
        [Range(0f, 1f)] public float aggressiveBelow = 0.33f;
        [Range(0f, 1f)] public float defensiveAbove = 0.66f;

        // Agent type id for this profile.
        public string AgentTypeId => "Enemy_" + name;
    }
}
