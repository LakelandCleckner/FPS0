using UnityEngine;
using Combat.Enemies;

namespace Combat.AI
{
    // Shooter presentation on the Animator object. Drives the player's third-person body controller (MoveX/MoveZ, Crouch,
    // Grounded, AimBlend) from the enemy's movement and weapon state, plus look-at IK (base layer needs IK Pass).
    [RequireComponent(typeof(Animator))]
    public class EnemyShooterAnimator : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] private string moveXParam = "MoveX";
        [SerializeField] private string moveZParam = "MoveZ";
        [SerializeField] private string crouchParam = "Crouch";
        [SerializeField] private string groundedParam = "Grounded";
        [SerializeField] private string aimBlendParam = "AimBlend";

        [Header("Locomotion")]
        [Tooltip("Speed that reads as 1 on MoveX/MoveZ: the walk clips. Twice this is the sprint clips (m/s).")]
        [SerializeField, Min(0.1f)] private float walkClipSpeed = 1.5f;
        [Tooltip("Largest MoveX/MoveZ length (2 = sprint).")]
        [SerializeField, Min(0.1f)] private float maxMove = 2f;
        [Tooltip("Smoothing on the move params. 0 = raw velocity.")]
        [SerializeField, Min(0f)] private float dampTime = 0.2f;
        [Tooltip("Horizontal speeds below this count as stopped.")]
        [SerializeField, Min(0f)] private float stopThreshold = 0.05f;

        [Header("Crouch")]
        [Tooltip("Seconds to crouch fully down behind cover.")]
        [SerializeField, Min(0.01f)] private float crouchDownTime = 0.45f;
        [Tooltip("Seconds to stand back up (to peek or move off).")]
        [SerializeField, Min(0.01f)] private float crouchUpTime = 0.3f;

        [Header("Weapon pose")]
        [Tooltip("Seconds to raise or lower the weapon pose (AimBlend).")]
        [SerializeField, Min(0.01f)] private float aimBlendTime = 0.2f;

        [Header("Look At")]
        [Tooltip("Head and body look-at IK toward the target. Off: the clips and the spine aim drive the upper body.")]
        [SerializeField] private bool lookAt = false;
        [Tooltip("Height on the target the head looks at (m).")]
        [SerializeField] private float targetLookHeight = 1.4f;
        [SerializeField, Range(0f, 1f)] private float bodyWeight = 0.35f;
        [SerializeField, Range(0f, 1f)] private float headWeight = 0.85f;
        [SerializeField, Range(0f, 1f)] private float eyesWeight = 0f;
        [Tooltip("0 = free, 1 = locked to the clip pose.")]
        [SerializeField, Range(0f, 1f)] private float clampWeight = 0.5f;
        [Tooltip("Seconds to blend the look in or out.")]
        [SerializeField, Min(0.01f)] private float lookBlend = 0.25f;

        // Set by actions (cover, 3b)
        public bool Crouched { get; set; }

        private Animator animator;
        private EnemyMover mover;
        private EnemyPerception perception;
        private EnemyRangedAttack ranged;
        private Transform root;
        private int moveXHash, moveZHash, crouchHash, groundedHash, aimBlendHash;
        private bool hasMoveX, hasMoveZ, hasCrouch, crouchIsFloat, hasGrounded, hasAimBlend;
        private float lookWeight, aimBlend, crouch;
        private Vector3 lookPoint;
        private Vector3 smoothVelocity, smoothAccel;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            mover = GetComponentInParent<EnemyMover>();
            perception = GetComponentInParent<EnemyPerception>();
            ranged = GetComponentInParent<EnemyRangedAttack>();
            root = mover != null ? mover.transform : transform;

            moveXHash = Animator.StringToHash(moveXParam);
            moveZHash = Animator.StringToHash(moveZParam);
            crouchHash = Animator.StringToHash(crouchParam);
            groundedHash = Animator.StringToHash(groundedParam);
            aimBlendHash = Animator.StringToHash(aimBlendParam);
            if (animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == moveXHash) hasMoveX = true;
                else if (p.nameHash == moveZHash) hasMoveZ = true;
                else if (p.nameHash == crouchHash) { hasCrouch = true; crouchIsFloat = p.type == AnimatorControllerParameterType.Float; }
                else if (p.nameHash == groundedHash) hasGrounded = p.type == AnimatorControllerParameterType.Bool;
                else if (p.nameHash == aimBlendHash) hasAimBlend = true;
            }
        }

        private void OnDisable()
        {
            lookWeight = aimBlend = crouch = 0f;
            Crouched = false;
            smoothVelocity = smoothAccel = Vector3.zero;
        }

        private void Update()
        {
            if (mover == null) return;
            float dt = Time.deltaTime;

            Vector3 v = mover.Velocity;
            v.y = 0f;
            if (v.magnitude < stopThreshold) v = Vector3.zero;

            // Smooth in world space, then project: turning alone doesn't jolt the blend
            smoothVelocity = dampTime > 0f ? Vector3.SmoothDamp(smoothVelocity, v, ref smoothAccel, dampTime, Mathf.Infinity, dt) : v;
            Vector3 move = Vector3.ClampMagnitude(root.InverseTransformDirection(smoothVelocity) / walkClipSpeed, maxMove);

            if (hasMoveX) animator.SetFloat(moveXHash, move.x);
            if (hasMoveZ) animator.SetFloat(moveZHash, move.z);
            if (hasCrouch)
            {
                // Ease into and out of the crouch blend instead of snapping
                crouch = Mathf.MoveTowards(crouch, Crouched ? 1f : 0f, dt / (Crouched ? crouchDownTime : crouchUpTime));
                if (crouchIsFloat) animator.SetFloat(crouchHash, Mathf.SmoothStep(0f, 1f, crouch));
                else animator.SetBool(crouchHash, crouch > 0.5f);
            }
            if (hasGrounded) animator.SetBool(groundedHash, true);

            if (hasAimBlend)
            {
                var phase = ranged != null ? ranged.CurrentPhase : EnemyRangedAttack.Phase.Lowered;
                bool raised = phase == EnemyRangedAttack.Phase.Raising || phase == EnemyRangedAttack.Phase.Ready
                              || phase == EnemyRangedAttack.Phase.Bursting;
                aimBlend = Mathf.MoveTowards(aimBlend, raised ? 1f : 0f, dt / aimBlendTime);
                animator.SetFloat(aimBlendHash, aimBlend);
            }

            Vector3 p = default;
            bool want = lookAt && TryGetLook(out p);
            if (want) lookPoint = p;
            lookWeight = Mathf.MoveTowards(lookWeight, want ? 1f : 0f, dt / lookBlend);
        }

        private bool TryGetLook(out Vector3 point)
        {
            point = default;
            if (mover.HasLook) { point = mover.LookPoint; return true; }
            if (perception != null && (perception.HasTarget || perception.HasLastKnown))
            {
                point = perception.BelievedPosition + Vector3.up * targetLookHeight;
                return true;
            }
            return false;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0) return;
            animator.SetLookAtWeight(lookWeight, bodyWeight, headWeight, eyesWeight, clampWeight);
            if (lookWeight > 0f) animator.SetLookAtPosition(lookPoint);
        }

        // Missing params the debug markers list
        public void CollectIssues(System.Collections.Generic.List<string> issues)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (!hasMoveX || !hasMoveZ) issues.Add($"Animator: no '{moveXParam}'/'{moveZParam}' floats");
            if (!hasAimBlend) issues.Add($"Animator: no '{aimBlendParam}' float (use the TP body controller)");
        }
    }
}
