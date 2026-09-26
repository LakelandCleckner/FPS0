using UnityEngine;
using UnityEngine.AI;

namespace Enemies.Animation
{
    // Presentation component: feeds the enemy's actual NavMeshAgent velocity into the
    // model's Animator. Knows nothing about GOAP or stats — AgentMoveBehaviour already
    // resolves nav.speed from move_speed x the action's multiplier, so reading real
    // velocity means wander/chase/slows all show up in the animation for free.
    //
    // Reads velocity (not desiredVelocity) so the blend ramps with acceleration instead
    // of snapping. Zero velocity (InvestigateAction look-around, halted rotate-to-target)
    // lands on idle.
    //
    // SETUP:
    //   Lives on the enemy root beside the NavMeshAgent. Animator is auto-found in
    //   children (the model). Apply Root Motion must be OFF on that Animator, or the
    //   animation and the agent fight over position.
    //
    //   Blend tree thresholds should match RESOLVED speeds:
    //     walk = move_speed, run = move_speed x chase_speed_multiplier.
    [RequireComponent(typeof(NavMeshAgent))]
    public class AlienZombieBaseLocomotionAnimator : MonoBehaviour
    {
        [Header("Refs (auto-found if empty)")]
        [SerializeField] private NavMeshAgent nav;
        [SerializeField] private Animator animator;

        [Header("Animator")]
        [SerializeField] private string speedParam = "Speed";

        [Tooltip("Smoothing on the Speed param. 0 = raw velocity.")]
        [SerializeField, Min(0f)] private float dampTime = 0.1f;

        [Tooltip("Horizontal speeds below this are treated as stopped, so a creeping " +
                 "agent doesn't hover between idle and walk.")]
        [SerializeField, Min(0f)] private float stopThreshold = 0.05f;

        private int speedHash;

        private void Awake()
        {
            if (nav == null) nav = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            speedHash = Animator.StringToHash(speedParam);

            if (animator == null)
                Debug.LogError("[AlienZombieBaseLocomotionAnimator] No Animator found in children.", this);
        }

        private void Update()
        {
            // Explicit null checks — ?. bypasses Unity's overloaded equality.
            if (animator == null || nav == null) return;

            float speed = 0f;
            if (nav.isActiveAndEnabled)
            {
                Vector3 v = nav.velocity;
                v.y = 0f;                       // slopes aren't speed
                speed = v.magnitude;
                if (speed < stopThreshold) speed = 0f;
            }

            if (dampTime > 0f)
                animator.SetFloat(speedHash, speed, dampTime, Time.deltaTime);
            else
                animator.SetFloat(speedHash, speed);
        }
    }
}
