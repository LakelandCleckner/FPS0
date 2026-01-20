using KBCore.Refs;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float baseWalkSpeed;
    [SerializeField] private float baseSprintMultiplier;


    [Header("Acceleration")]
    [SerializeField] private float groundAcceleration = 25f;
    [SerializeField] private float airAcceleration = 6f;


    //[Header("Stats")] TO BE POSSIBLY ADDED LATER AFTER DEALING WITH MOVEMENT AND OTHER BASIC NEEDS
    /*[SerializeField] private int baseStamina = 100;
    [SerializeField, Self] private Stamina stamina;
    private int currentStamina; */

    [Header("Jump")]
    [SerializeField] private float jumpForce;
    [SerializeField] private float gravity;
    [SerializeField] private float jumpBufferTime = 0.1f;
    private float jumpBufferCounter;

    [Header("Sprint Rules")]
    [SerializeField] private float sprintTakeoffGraceTime = 0.12f; // press sprint slightly before jumping
    [SerializeField] private float sprintForwardThreshold = 0.1f;  // require forward input to sprint

    private float sprintGraceCounter;
    private bool wantsToSprint;     // intent (button held)
    private bool airSprintAllowed;  // locked-in sprint momentum for airtime
    private bool wasGrounded;

    private Vector3 currentMovement = Vector3.zero;

    [SerializeField, Self] private CharacterController characterController;

    private PlayerInputs playerInputs;

    [Header("New Input Actions")]
    [SerializeField] private Vector2 moveInput;

    [Header("References")]
    [SerializeField] private MouseLook mouseLook;

    /// <summary>
    /// Handles player Movement, Jumping, and Gravity
    /// Takes a reference from MouseLook.cs to handle mouse control.
    /// </summary>
    private void OnValidate()
    {
        this.ValidateRefs();
    }

    private void Awake()
    {
        playerInputs = new PlayerInputs();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        //currentStamina = baseStamina; for Undecided Stamina System

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
        // Count down grace window
        if (sprintGraceCounter > 0f)
        {
            sprintGraceCounter -= Time.deltaTime;
            if (sprintGraceCounter < 0f) sprintGraceCounter = 0f;
        }

        bool grounded = characterController.isGrounded;

        // On takeoff, lock whether sprint is allowed for this airtime
        if (wasGrounded && !grounded)
        {
            bool hasForwardInput = moveInput.y > sprintForwardThreshold;
            bool sprintAtTakeoff = (wantsToSprint || sprintGraceCounter > 0f) && hasForwardInput;

            airSprintAllowed = sprintAtTakeoff;
        }

        // On landing, clear air lock
        if (!wasGrounded && grounded)
        {
            airSprintAllowed = false;
        }

        wasGrounded = grounded;
    }

    private void HandleMovement()
    {
        bool grounded = characterController.isGrounded;
        bool hasForwardInput = moveInput.y > sprintForwardThreshold;

        // Sprint rules
        bool groundSprintAllowed = wantsToSprint && hasForwardInput;
        bool isSprintingNow = grounded ? groundSprintAllowed : airSprintAllowed;

        float speedMultiplier = isSprintingNow ? baseSprintMultiplier : 1f;

        // Desired horizontal velocity (world space)
        float desiredForward = moveInput.y * baseWalkSpeed * speedMultiplier;
        float desiredStrafe = moveInput.x * baseWalkSpeed * speedMultiplier;

        Vector3 desiredHorizontal = new Vector3(desiredStrafe, 0f, desiredForward);
        desiredHorizontal = transform.rotation * desiredHorizontal;

        // Current horizontal velocity (world space)
        Vector3 currentHorizontal = new Vector3(currentMovement.x, 0f, currentMovement.z);

        // Blend factor based on acceleration
        float accel = grounded ? groundAcceleration : airAcceleration;

        // MoveTowards gives a “speed per second” style acceleration (very controllable)
        currentHorizontal = Vector3.MoveTowards(
            currentHorizontal,
            desiredHorizontal,
            accel * Time.deltaTime
        );

        // Apply blended horizontal back into movement
        currentMovement.x = currentHorizontal.x;
        currentMovement.z = currentHorizontal.z;

        HandleJump();
        HandleGravity();

        characterController.Move(currentMovement * Time.deltaTime);
    }


    /*private void HandleStaminaUsage(Vector3 movement)
    {
        if (movement != Vector3.zero && isSprintPressed)
        {
            currentStamina -= 1;
        }
        else
        {
            currentStamina += 1;
        }
        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);
        stamina.UpdateStamina(currentStamina);
    }*/

    private void HandleJump()
    {
        if (!characterController.isGrounded)
            return;

        if (jumpBufferCounter > 0f)
        {
            currentMovement.y = jumpForce;
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
            currentMovement.y -= gravity * Time.deltaTime;
        }
    }

    private void Sprint(InputAction.CallbackContext ctx)
    {
        wantsToSprint = ctx.ReadValueAsButton();

        // If sprint is pressed, start/refresh the grace timer
        if (wantsToSprint)
        {
            sprintGraceCounter = sprintTakeoffGraceTime;
        }
    }
}
