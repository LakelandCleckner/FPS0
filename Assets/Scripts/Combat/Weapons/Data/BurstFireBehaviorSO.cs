using UnityEngine;

namespace Combat.Weapons
{
    // Authored burst parameters. RPM stays on the archetype (it's a stat, so it can be
    // modified); shots-per-burst, intra-burst rate and the hold behaviour live here
    // because they are behaviour-specific — "conditional stats" that only exist for
    // the behaviour that needs them, exactly like charge time will be.
    [CreateAssetMenu(fileName = "BurstFire", menuName = "Combat/Weapons/Fire Behavior/Burst")]
    public class BurstFireBehaviorSO : FireBehaviorSO
    {
        [Tooltip("Shots per trigger pull. Each one consumes ammo and publishes its own " +
                 "ShotFired — a 3-round burst is three shots, not one.")]
        public int shotsPerBurst = 3;

        [Tooltip("Seconds between shots WITHIN a burst. Pure feel: the burst-to-burst " +
                 "cooldown absorbs this, so changing it makes the burst tighter or " +
                 "looser WITHOUT changing the weapon's sustained rate.\n\n" +
                 "It has a ceiling of 60/RPM — past that the burst fills its whole " +
                 "cycle, the pause reaches zero, and the weapon fires as a continuous " +
                 "stream. Well before the ceiling the burst stops reading as a burst: " +
                 "what makes one audible is the PAUSE being several times the gap " +
                 "inside it, so aim for a delay well under a third of the ceiling.")]
        public float intraBurstDelay = 0.06f;

        [Tooltip("Keep firing bursts while the trigger is held. Off = one burst per " +
                 "pull, which needs a deliberate click for each burst.")]
        public bool repeatWhileHeld = true;

        public override IFireBehavior CreateBehavior()
            => new BurstFireBehavior(shotsPerBurst, intraBurstDelay, repeatWhileHeld);
    }
}