using UnityEngine;

// Per-frame snapshot the body reads; the state to replicate later
public struct PresentationState
{
    public Vector2 Move;          // local x/z: 1 = walk, 2 = sprint
    public bool Grounded;
    public float VerticalSpeed;
    public float Crouch;          // 0 standing .. 1 crouched
    public float Pitch;           // degrees, positive looks down
    public bool Aiming;
    public float AimBlend;
}

// Samples local player systems after they update
[DefaultExecutionOrder(100)]
public class PlayerPresentation : MonoBehaviour
{
    [Header("Refs (found automatically if empty)")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerCrouch crouch;
    [SerializeField] private PlayerAim aim;
    [SerializeField] private MouseLook look;

    public PresentationState State { get; private set; }

    private void Awake()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (crouch == null) crouch = GetComponent<PlayerCrouch>();
        if (aim == null) aim = GetComponent<PlayerAim>();
        if (look == null) look = GetComponentInChildren<MouseLook>();
    }

    private void Update()
    {
        var s = new PresentationState();

        if (movement != null)
        {
            s.Move = NormalizedMove(movement.LocalVelocity);
            s.Grounded = movement.IsGrounded;
            s.VerticalSpeed = movement.VerticalVelocity;
        }
        if (crouch != null) s.Crouch = crouch.Blend;
        if (aim != null)
        {
            s.Aiming = aim.IsAiming;
            s.AimBlend = aim.Blend;
        }
        if (look != null) s.Pitch = look.Pitch;

        State = s;
    }

    // Same scale as the arms Speed param, kept on the move direction
    private Vector2 NormalizedMove(Vector3 local)
    {
        var flat = new Vector2(local.x, local.z);
        float speed = flat.magnitude;
        if (speed < 0.01f) return Vector2.zero;

        float walk = Mathf.Max(0.01f, movement.WalkSpeed);
        float sprintRange = Mathf.Max(0.01f, walk * (movement.SprintMultiplier - 1f));
        float value = speed <= walk ? speed / walk : 1f + (speed - walk) / sprintRange;

        return flat / speed * Mathf.Clamp(value, 0f, 2f);
    }
}
