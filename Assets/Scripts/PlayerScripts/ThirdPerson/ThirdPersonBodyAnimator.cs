using UnityEngine;

// Drives the third-person body Animator from PlayerPresentation
[DefaultExecutionOrder(110)]
public class ThirdPersonBodyAnimator : MonoBehaviour
{
    [Header("Refs (found automatically if empty)")]
    [SerializeField] private PlayerPresentation presentation;
    [SerializeField] private Animator animator;

    [Header("Tuning")]
    [Tooltip("Damp time for the locomotion blend.")]
    [SerializeField] private float moveDamp = 0.1f;
    [Tooltip("Airborne time before Grounded drops, hides slope flicker.")]
    [SerializeField] private float airDelay = 0.1f;

    private static readonly int MoveXParam = Animator.StringToHash("MoveX");
    private static readonly int MoveZParam = Animator.StringToHash("MoveZ");
    private static readonly int CrouchParam = Animator.StringToHash("Crouch");
    private static readonly int GroundedParam = Animator.StringToHash("Grounded");
    private static readonly int VerticalSpeedParam = Animator.StringToHash("VerticalSpeed");
    private static readonly int AimBlendParam = Animator.StringToHash("AimBlend");

    private float airTime;

    private void Awake()
    {
        if (presentation == null) presentation = GetComponentInParent<PlayerPresentation>();
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) return;

        animator.applyRootMotion = false;                          // movement is code-driven
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;  // owner body is shadows-only
    }

    private void Update()
    {
        if (presentation == null || animator == null) return;

        var s = presentation.State;
        float dt = Time.deltaTime;

        airTime = s.Grounded ? 0f : airTime + dt;

        animator.SetFloat(MoveXParam, s.Move.x, moveDamp, dt);
        animator.SetFloat(MoveZParam, s.Move.y, moveDamp, dt);
        animator.SetFloat(CrouchParam, s.Crouch);
        animator.SetBool(GroundedParam, airTime < airDelay);
        animator.SetFloat(VerticalSpeedParam, s.VerticalSpeed);
        animator.SetFloat(AimBlendParam, s.AimBlend);
    }
}
