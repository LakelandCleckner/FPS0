using UnityEngine;
using Combat.Core;

namespace Combat.Enemies
{
    // Per-rank stagger tuning
    [System.Serializable]
    public struct StaggerTuning
    {
        [Tooltip("Off: only the procedural kick, never a stagger.")]
        public bool canStagger;
        [Tooltip("Off: overloads become normal staggers.")]
        public bool canHeavyStagger;
        [Tooltip("Meter (share of max health x stagger power) that triggers a stagger.")]
        public float threshold;
        [Tooltip("Meter above threshold x this triggers a heavy stagger.")]
        public float overloadMultiple;
        [Tooltip("Multiplier on stagger times.")]
        public float timeScale;
        [Tooltip("Multiplier on the procedural kick.")]
        public float kickScale;

        public static StaggerTuning Make(float threshold, float overload, float time, float kick, bool heavy = true)
            => new StaggerTuning { canStagger = true, canHeavyStagger = heavy, threshold = threshold,
                                   overloadMultiple = overload, timeScale = time, kickScale = kick };
    }

    // How an enemy flinches and staggers when hit
    [CreateAssetMenu(fileName = "HitReaction", menuName = "Combat/Enemies/Hit Reaction Profile")]
    public class HitReactionProfileSO : ScriptableObject
    {
        [Header("Sources")]
        [Tooltip("Bullets and melee.")]
        public bool direct = true;
        [Tooltip("Explosions and chain hits.")]
        public bool chain = true;
        [Tooltip("Shield damage counts toward stagger. Off: only damage that reaches health.")]
        public bool shieldDamageStaggers = false;

        [Header("Meter")]
        [Tooltip("Seconds after the last hit before the meter drains.")]
        public float drainDelay = 0.4f;
        [Tooltip("Meter drained per second, as a share of the threshold.")]
        public float drainRate = 1f;

        [Header("Stagger")]
        [Tooltip("Seconds a stagger stops movement and attacks.")]
        public float staggerTime = 0.55f;
        [Tooltip("Seconds a heavy stagger stops movement and attacks.")]
        public float heavyStaggerTime = 1.1f;
        [Tooltip("Extra seconds before firing again after a stagger.")]
        public float fireRecovery = 0.25f;
        [Tooltip("Seconds every hit stops movement, scaled by the rank's time scale. 0 = hits never stop movement.")]
        public float flinchStopTime = 0f;
        [Tooltip("Seconds after a stagger ends before another (heavy staggers ignore this).")]
        public float immunityTime = 0.8f;
        [Tooltip("A stagger cancels a melee swing past its wind-up too. Off: only wind-ups.")]
        public bool staggerCancelsStrike = false;
        [Tooltip("A heavy stagger cancels a reload.")]
        public bool heavyCancelsReload = true;

        [Header("Clips")]
        [Tooltip("Seconds to blend a hit clip in.")]
        public float blendIn = 0.08f;
        [Tooltip("Seconds to blend a hit clip out at its end.")]
        public float blendOut = 0.25f;
        [Tooltip("Hit clip speed.")]
        public float clipSpeed = 1f;
        [Tooltip("Horizontal speed that picks the walking clip (m/s).")]
        public float walkClipSpeed = 0.5f;
        [Tooltip("Horizontal speed that picks the running clip (m/s).")]
        public float runClipSpeed = 2.5f;

        [Header("Kick")]
        [Tooltip("Peak kick of any hit (degrees).")]
        public float kickBase = 3f;
        [Tooltip("Added peak per share of max health dealt (degrees; 60 = +6 for a 10% hit).")]
        public float kickPerHealth = 60f;
        [Tooltip("Largest peak (degrees).")]
        public float kickMax = 14f;
        [Tooltip("Spring stiffness: higher settles faster.")]
        public float kickStiffness = 140f;
        [Tooltip("Spring damping: below 2 x sqrt(stiffness) bounces a little.")]
        public float kickDamping = 20f;
        [Tooltip("Kick scale on limb hits.")]
        public float limbKickScale = 0.6f;

        [Header("Aim")]
        [Tooltip("Aim cone added per hit (degrees).")]
        public float aimJolt = 1.5f;
        [Tooltip("Largest added aim cone (degrees).")]
        public float aimJoltMax = 6f;
        [Tooltip("Seconds the added aim cone takes to settle.")]
        public float aimJoltSettle = 0.4f;

        [Header("Ranks")]
        public StaggerTuning minor = StaggerTuning.Make(0.15f, 2f, 1f, 1f);
        public StaggerTuning major = StaggerTuning.Make(0.2f, 2f, 0.9f, 0.85f);
        public StaggerTuning elite = StaggerTuning.Make(0.25f, 2.5f, 0.8f, 0.7f);
        public StaggerTuning miniboss = StaggerTuning.Make(0.35f, 3f, 0.6f, 0.5f);
        public StaggerTuning boss = StaggerTuning.Make(0.5f, 3f, 0.5f, 0.35f, false);

        public StaggerTuning For(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.Major: return major;
                case EnemyRank.Elite: return elite;
                case EnemyRank.Miniboss: return miniboss;
                case EnemyRank.Boss: return boss;
                default: return minor;
            }
        }

        public bool Accepts(HitSource source) => source == HitSource.Direct ? direct : source == HitSource.Chain && chain;
    }
}
