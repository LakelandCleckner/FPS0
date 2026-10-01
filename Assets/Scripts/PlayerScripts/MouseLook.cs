using UnityEngine;

public class MouseLook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform playerBody;   // Yaw (left/right)
    [SerializeField] private Camera playerCamera;    // Pitch (up/down)

    [Header("Settings")]
    [Tooltip("Hip sensitivity comes from here when assigned.")]
    [SerializeField] private GameSettingsSO settings;

    [Header("Sensitivity (degrees per mouse unit) — fallback when no settings asset")]
    [SerializeField] private float hipSensitivity = 0.1f;

    [Header("Pitch Clamp")]
    [SerializeField] private float upDownRange = 85f;

    private float verticalRotation;
    private Vector2 lookInput;

    // Set every frame by PlayerAim (1 at hip). Follows the live FOV, so it's
    // correct mid-transition, not just at full ADS.
    public float SensitivityScale { get; set; } = 1f;

    /// <summary>
    /// Called by PlayerMovement when look input is received.
    /// </summary>
    public void SetLookInput(Vector2 input)
    {
        lookInput = input;
    }

    private void Update()
    {
        ApplyLook();
    }

    private void ApplyLook()
    {
        float sens = GetHipSensitivity() * SensitivityScale;

        float mouseX = lookInput.x * sens;
        float mouseY = lookInput.y * sens;

        // Horizontal rotation (yaw)
        playerBody.Rotate(Vector3.up * mouseX);

        // Vertical rotation (pitch)
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -upDownRange, upDownRange);

        playerCamera.transform.localRotation =
            Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    // ===== UI / Settings hooks =====

    public void SetHipSensitivity(float value)
    {
        if (settings != null) settings.hipSensitivity = value;
        else hipSensitivity = value;
    }

    public float GetHipSensitivity()
    {
        return settings != null ? settings.hipSensitivity : hipSensitivity;
    }
}