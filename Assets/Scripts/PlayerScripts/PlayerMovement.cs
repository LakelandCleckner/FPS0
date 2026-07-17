using KBCore.Refs;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Stats;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement (tuning — pushed into the stat container as bases at Start)")]
    [SerializeField] private float baseWalkSpeed;
    [SerializeField] private float baseSprintMultiplier;

    [Header("Acceleration (tuning — pushed as bases)")]
    [SerializeField] private float groundAcceleration = 25f;
    [SerializeField] private float airAcceleration = 6f;

    [Header("Jump / Gravity (tuning — jumpForce + gravity pushed as bases)")]
    [SerializeField] private float jumpForce;
    [SerializeField] private float gravity;
    [SerializeField] private float jumpBufferTime = 0.1f;   // input feel — NOT a stat
    private float jumpBufferCounter;

    [Header("Sprint Rules (input feel — NOT stats)")]
    [SerializeField] private float sprintTakeoffGraceTime = 0.12f;
    [SerializeField] private float sprintForwardThreshold = 0.1f;

    [Header("Stats")]
    [Tooltip("The player's stat container (its CombatantStats). Movement stats live here.")]
    [SerializeField] private CombatantStats combatantStats;
    [Tooltip("References to the movement stat definitions.")]
    [SerializeField] private MovementStatKeys statKeys;

    private float sprintGraceCounter;
    private bool wantsToSprint;
    private bool airSprintAllowed;
    private bool wasGrounded;

    private Vector3 currentMovement = Vector3.zero;

    [SerializeField, Self] private CharacterController characterController;

    private PlayerInputs playerInputs;

    [Header("New Input Actions")]
    [SerializeField] private Vector2 moveInput;

    [Header("References")]
    [SerializeField] private MouseLook mouseLook;

    private void OnValidate()
    {
        this.ValidateRefs();
    }

    private void Awake()
    {
        playerInputs = new PlayerInputs();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (combatantStats == null)
            combatantStats = GetComponent<CombatantStats>();

        playerInputs.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        playerInputs.Player.Move.canceled += _ => moveInput = Vector2.zero;

        playerInputs.Player.Look.performed += ctx => mouseLook.SetLookInput(ctx.ReadValue<Vector2>());
        playerInputs.Player.Look.canceled += _ => mouseLook.SetLookInput(Vector2.zero);

        playerInputs.Player.Jump.started += ctx =>
        {
            jumpBufferCounter = jumpBufferTime;
        };

        playerInputs.Player.Run.started += Sprint;
        playerInputs.Player.Run.canceled += Sprint;
    }

    private void Start()
    {
        // Push the serialized tuning values into the container as the stat BASES.
        // (Author on this component; the container is what movement actually reads,
        // so modifiers — slows, speed perks, low-grav — layer on top.)
        PushBases();
    }

    private void PushBases()
    {
        var c = combatantStats != null ? combatantStats.Container : null;
        if (c == null || statKeys == null) return;

        if (statKeys.moveSpeed != null) c.SetBase(statKeys.moveSpeed, baseWalkSpeed);
        if (statKeys.sprintMultiplier != null) c.SetBase(statKeys.sprintMultiplier, baseSprintMultiplier);
        if (statKeys.jumpForce != null) c.SetBase(statKeys.jumpForce, jumpForce);
        if (statKeys.groundAcceleration != null) c.SetBase(statKeys.groundAcceleration, groundAcceleration);
        if (statKeys.airAcceleration != null) c.SetBase(statKeys.airAcceleration, airAcceleration);
        if (statKeys.gravity != null) c.SetBase(statKeys.gravity, gravity);
    }

    // Resolve a movement stat live (cached). Falls back to the serialized tuning
    // value if the container isn't ready (defensive — no zero-speed race).
    private float Stat(StatDefinitionSO def, float fallback)
    {
        var c = combatantStats != null ? combatantStats.Container : null;
        if (c == null || def == null) return fallback;
        return c.Resolve(def);
    }

    private void Update()
    {
        UpdateSprintState();

        HandleMovement();

        if (jumpBufferCounter > 0f)
        {
            jumpBufferCounter -= Time.deltaTime;
            if (jumpBufferCounter < 0f) jumpBufferCounter = 0f;
        }
    }

    private void OnEnable() => playerInputs.Enable();
    private void OnDisable() => playerInputs.Disable();

    private void UpdateSprintState()
    {
        if (sprintGraceCounter > 0f)
        {
            sprintGraceCounter -= Time.deltaTime;
            if (sprintGraceCounter < 0f) sprintGraceCounter = 0f;
        }

        bool grounded = characterController.isGrounded;

        if (wasGrounded && !grounded)
        {
            bool hasForwardInput = moveInput.y > sprintForwardThreshold;
            bool sprintAtTakeoff = (wantsToSprint || sprintGraceCounter > 0f) && hasForwardInput;
            airSprintAllowed = sprintAtTakeoff;
        }

        if (!wasGrounded && grounded)
            airSprintAllowed = false;

        wasGrounded = grounded;
    }

    private void HandleMovement()
    {
        bool grounded = characterController.isGrounded;
        bool hasForwardInput = moveInput.y > sprintForwardThreshold;

        bool groundSprintAllowed = wantsToSprint && hasForwardInput;
        bool isSprintingNow = grounded ? groundSprintAllowed : airSprintAllowed;

        // resolved movement stats (live — a slow/speed modifier applies immediately)
        float moveSpeed = Stat(statKeys != null ? statKeys.moveSpeed : null, baseWalkSpeed);
        float sprintMult = Stat(statKeys != null ? statKeys.sprintMultiplier : null, baseSprintMultiplier);

        float speedMultiplier = isSprintingNow ? sprintMult : 1f;

        float desiredForward = moveInput.y * moveSpeed * speedMultiplier;
        float desiredStrafe = moveInput.x * moveSpeed * speedMultiplier;

        Vector3 desiredHorizontal = new Vector3(desiredStrafe, 0f, desiredForward);
        desiredHorizontal = transform.rotation * desiredHorizontal;

        Vector3 currentHorizontal = new Vector3(currentMovement.x, 0f, currentMovement.z);

        float groundAccel = Stat(statKeys != null ? statKeys.groundAcceleration : null, groundAcceleration);
        float airAccel = Stat(statKeys != null ? statKeys.airAcceleration : null, airAcceleration);
        float accel = grounded ? groundAccel : airAccel;

        currentHorizontal = Vector3.MoveTowards(
            currentHorizontal,
            desiredHorizontal,
            accel * Time.deltaTime
        );

        currentMovement.x = currentHorizontal.x;
        currentMovement.z = currentHorizontal.z;

        HandleJump();
        HandleGravity();

        characterController.Move(currentMovement * Time.deltaTime);
    }

    private void HandleJump()
    {
        if (!characterController.isGrounded)
            return;

        if (jumpBufferCounter > 0f)
        {
            currentMovement.y = Stat(statKeys != null ? statKeys.jumpForce : null, jumpForce);
            jumpBufferCounter = 0f;
        }
    }

    private void HandleGravity()
    {
        if (characterController.isGrounded && currentMovement.y < 0f)
        {
            currentMovement.y = -2f; // ground stick
        }
        else
        {
            float g = Stat(statKeys != null ? statKeys.gravity : null, gravity);
            currentMovement.y -= g * Time.deltaTime;
        }
    }

    private void Sprint(InputAction.CallbackContext ctx)
    {
        wantsToSprint = ctx.ReadValueAsButton();
        if (wantsToSprint)
            sprintGraceCounter = sprintTakeoffGraceTime;
    }

    public float CurrentSpeed => new Vector3(currentMovement.x, 0f, currentMovement.z).magnitude;
    public bool IsGrounded => characterController.isGrounded;
}