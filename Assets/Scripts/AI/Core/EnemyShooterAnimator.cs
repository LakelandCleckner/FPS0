using UnityEngine;

namespace Combat.AI
{
    // Shooter presentation on the Animator object: strafe blend params and look-at IK (base layer needs IK Pass).
    [RequireComponent(typeof(Animator))]
    public class EnemyShooterAnimator : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] private string moveXParam = "MoveX";
        [SerializeField] private string moveZParam = "MoveZ";
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string crouchParam = "Crouch";
        [Tooltip("Smoothing on the move params. 0 = raw velocity.")]
        [SerializeField, Min(0f)] private float dampTime = 0.12f;
        [Tooltip("Horizontal speeds below this count as stopped.")]
        [SerializeField, Min(0f)] private float stopThreshold = 0.05f;

        [Header("Look At")]
        [Tooltip("Height on the target the head looks at (m).")]
        [SerializeField] private float targetLookHeight = 1.4f;
        [SerializeField, Range(0f, 1f)] private float bodyWeight = 0.35f;
        [SerializeField, Range(0f, 1f)] private float headWeight = 0.85f;
        [SerializeField, Range(0f, 1f)] private float eyesWeight = 0f;
        [Tooltip("0 = free, 1 = locked to the clip pose.")]
        [SerializeField, Range(0f, 1f)] private float clampWeight = 0.5f;
        [Tooltip("Seconds to blend the look in or out.")]
        [SerializeField, Min(0.01f)] private float lookBlend = 0.25f;

        // Set by actions (cover, 3b); the controller's crouch layer reads it.
        public bool Crouched { get; set; }

        private Animator animator;
        private EnemyMover mover;
        private EnemyPerception perception;
        private Transform root;
        private int moveXHash, moveZHash, speedHash, crouchHash;
        private bool hasMoveX, hasMoveZ, hasSpeed, hasCrouch;
        private float lookWeight;
        private Vector3 lookPoint;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            mover = GetComponentInParent<EnemyMover>();
            perception = GetComponentInParent<EnemyPerception>();
            root = mover != null ? mover.transform : transform;

            moveXHash = Animator.StringToHash(moveXParam);
            moveZHash = Animator.StringToHash(moveZParam);
            speedHash = Animator.StringToHash(speedParam);
            crouchHash = Animator.StringToHash(crouchParam);
            if (animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == moveXHash) hasMoveX = true;
                else if (p.nameHash == moveZHash) hasMoveZ = true;
                else if (p.nameHash == speedHash) hasSpeed = true;
                else if (p.nameHash == crouchHash) hasCrouch = true;
            }
        }

        private void OnDisable()
        {
            lookWeight = 0f;
            Crouched = false;
        }

        private void Update()
        {
            if (mover == null) return;

            Vector3 v = mover.Velocity;
            v.y = 0f;
            if (v.magnitude < stopThreshold) v = Vector3.zero;
            Vector3 local = root.InverseTransformDirection(v);

            float dt = Time.deltaTime;
            if (hasMoveX) animator.SetFloat(moveXHash, local.x, dampTime, dt);
            if (hasMoveZ) animator.SetFloat(moveZHash, local.z, dampTime, dt);
            if (hasSpeed) animator.SetFloat(speedHash, v.magnitude, dampTime, dt);
            if (hasCrouch) animator.SetBool(crouchHash, Crouched);

            bool want = TryGetLook(out var p);
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
        }
    }
}
