using UnityEngine;
using Combat.Stats;

// Crouch: shrinks the CharacterController from the top (feet stay planted), lowers
// CameraRoot by the same amount, and applies a move_speed modifier.
//
// Standing up is blocked while there's geometry above; the player stays crouched and
// stands the moment there's room. Crouching cancels a sprint; pressing sprint while
// crouched stands you up (if there's room) — a held crouch button must be re-pressed.
[RequireComponent(typeof(CharacterController))]
public class PlayerCrouch : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GameSettingsSO settings;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private Transform cameraRoot;

    [Header("Stats")]
    [SerializeField] private CombatantStats combatantStats;
    [SerializeField] private MovementStatKeys movementStatKeys;

    [Header("Tuning")]
    [SerializeField] private float crouchHeight = 1.2f;
    [Tooltip("Seconds to go fully down or fully up.")]
    [SerializeField] private float transitionTime = 0.15f;
    [Tooltip("move_speed multiplier while crouched.")]
    [SerializeField] private float crouchSpeedMultiplier = 0.55f;
    [Tooltip("What blocks standing up. The player's own layer is always excluded.")]
    [SerializeField] private LayerMask ceilingMask = ~0;

    public bool IsCrouched { get; private set; }
    public float Blend { get; private set; }     // 0 standing .. 1 fully crouched

    private CharacterController cc;
    private PlayerInputs input;
    private readonly InputLatch latch = new InputLatch();
    private ModifierHandle moveHandle = ModifierHandle.None;

    private float standHeight;
    private Vector3 standCenter;
    private float bottomY;          // local Y of the capsule's bottom — held fixed
    private float standCamY;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        input = new PlayerInputs();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (combatantStats == null) combatantStats = GetComponent<CombatantStats>();

        standHeight = cc.height;
        standCenter = cc.center;
        bottomY = standCenter.y - standHeight * 0.5f;
        if (cameraRoot != null) standCamY = cameraRoot.localPosition.y;
    }

    private void OnEnable() => input.Enable();
    private void OnDisable()
    {
        input.Disable();
        SetMovePenalty(false);
        if (movement != null) movement.SprintBlocked = false;
    }

    private void Update()
    {
        if (settings == null) return;

        latch.Update(input.Player.Crouch.IsPressed(), settings.crouchInputMode, settings.hybridHoldThreshold);

        // Crouching drops a sprint.
        if (latch.PressedThisFrame && movement != null && movement.IsSprinting)
            movement.CancelSprint();

        // Sprint while crouched = stand up and go.
        if (IsCrouched && movement != null && movement.WantsToSprint && CanStand())
            latch.ForceOff();

        bool want = latch.Active;
        if (!want && IsCrouched && !CanStand()) want = true;   // no headroom

        if (want != IsCrouched)
        {
            IsCrouched = want;
            SetMovePenalty(want);
        }
        if (movement != null) movement.SprintBlocked = IsCrouched;

        float rate = 1f / Mathf.Max(0.01f, transitionTime);
        Blend = Mathf.MoveTowards(Blend, IsCrouched ? 1f : 0f, rate * Time.deltaTime);
        ApplyHeight();
    }

    private void ApplyHeight()
    {
        float h = Mathf.Lerp(standHeight, Mathf.Max(crouchHeight, cc.radius * 2f), Blend);
        cc.height = h;
        cc.center = new Vector3(standCenter.x, bottomY + h * 0.5f, standCenter.z);

        if (cameraRoot != null)
        {
            var p = cameraRoot.localPosition;
            p.y = standCamY - (standHeight - h);
            cameraRoot.localPosition = p;
        }
    }

    // Would the full standing capsule fit here?
    private bool CanStand()
    {
        float r = cc.radius;
        Vector3 bottom = transform.TransformPoint(new Vector3(standCenter.x, bottomY, standCenter.z));
        Vector3 up = transform.up;
        Vector3 p1 = bottom + up * (r + cc.skinWidth);
        Vector3 p2 = bottom + up * (standHeight - r);
        int mask = ceilingMask & ~(1 << gameObject.layer);
        return !Physics.CheckCapsule(p1, p2, r * 0.95f, mask, QueryTriggerInteraction.Ignore);
    }

    private void SetMovePenalty(bool on)
    {
        var c = combatantStats != null ? combatantStats.Container : null;
        if (c == null || movementStatKeys == null || movementStatKeys.moveSpeed == null) return;

        if (moveHandle.IsValid)
        {
            c.RemoveModifier(moveHandle);
            moveHandle = ModifierHandle.None;
        }

        if (on && !Mathf.Approximately(crouchSpeedMultiplier, 1f))
            moveHandle = c.AddModifier(
                new StatModifier(movementStatKeys.moveSpeed, StatResolver.MULTIPLICATIVE, crouchSpeedMultiplier - 1f), this);
    }
}
