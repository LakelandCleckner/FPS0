using UnityEngine;

public class MouseLook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform playerBody;   // Yaw (left/right)
    [SerializeField] private Camera playerCamera;    // Pitch (up/down)

    [Header("Sensitivity (degrees per mouse unit)")]
    [SerializeField] private float hipSensitivity = 0.1f;

    [Header("Pitch Clamp")]
    [SerializeField] private float upDownRange = 85f;

    private float verticalRotation;
    private Vector2 lookInput;

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
        // Raw mouse delta scaled by sensitivity
        float mouseX = lookInput.x * hipSensitivity;
        float mouseY = lookInput.y * hipSensitivity;

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
        hipSensitivity = value;
    }

    public float GetHipSensitivity()
    {
        return hipSensitivity;
    }
}
