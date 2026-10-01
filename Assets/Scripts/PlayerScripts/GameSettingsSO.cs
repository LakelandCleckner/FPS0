using System;
using UnityEngine;

// Player-facing settings. Every either/or choice is a player option.
// Runtime object only — persistence (save/load) and the settings UI come later.
// Components reference this asset and read it live, so a change applies next frame.
[CreateAssetMenu(fileName = "GameSettings", menuName = "Settings/Game Settings")]
public class GameSettingsSO : ScriptableObject
{
    public enum FovAxis { Horizontal, Vertical }
    public enum AdsSensitivityMode { MatchFov, Flat }

    [Header("Field of View")]
    [Tooltip("World FOV in degrees, measured on the axis below.")]
    [Range(60f, 130f)] public float fov = 90f;
    [Tooltip("Which axis the FOV value means. Horizontal is converted using the live aspect ratio.")]
    public FovAxis fovAxis = FovAxis.Horizontal;
    [Tooltip("Vertical FOV the arms/gun render at. Fixed — never changes with ADS zoom.")]
    [Range(30f, 90f)] public float viewmodelFov = 50f;

    [Header("Sensitivity")]
    [Tooltip("Degrees per mouse unit when not aiming.")]
    public float hipSensitivity = 0.1f;
    public AdsSensitivityMode adsSensitivityMode = AdsSensitivityMode.MatchFov;
    [Tooltip("MatchFov only. 0 = tan-ratio match (same screen distance per mouse inch). " +
             "Non-zero = monitor-distance match at that fraction of screen height (1 = 100%).")]
    [Range(0f, 2f)] public float monitorDistance = 0f;
    [Tooltip("Flat only. Sensitivity multiplier at full ADS.")]
    public float adsFlatMultiplier = 0.7f;
    [Tooltip("Applied on top of either mode at full ADS.")]
    public float adsSensitivityMultiplier = 1f;

    [Header("Input Modes")]
    public InputMode aimInputMode = InputMode.Hold;
    public InputMode crouchInputMode = InputMode.Hold;
    [Tooltip("Hybrid: holding longer than this acts as Hold; a shorter tap toggles.")]
    [Range(0.05f, 0.5f)] public float hybridHoldThreshold = 0.2f;

    // World vertical FOV for the current aspect ratio.
    public float WorldVerticalFov(float aspect)
    {
        if (fovAxis == FovAxis.Vertical) return fov;
        float h = fov * Mathf.Deg2Rad;
        return 2f * Mathf.Atan(Mathf.Tan(h * 0.5f) / Mathf.Max(0.01f, aspect)) * Mathf.Rad2Deg;
    }
}

public enum InputMode { Hold, Toggle, Hybrid }

// Turns a raw button into "is this action on" under Hold / Toggle / Hybrid.
public sealed class InputLatch
{
    private bool latched;
    private bool held;
    private bool heldLast;
    private float pressTime;
    private bool blockUntilRelease;

    public bool Active { get; private set; }
    public bool PressedThisFrame { get; private set; }

    public void Update(bool heldNow, InputMode mode, float hybridThreshold)
    {
        held = heldNow;
        PressedThisFrame = held && !heldLast;
        bool released = !held && heldLast;
        heldLast = held;

        if (released) blockUntilRelease = false;

        switch (mode)
        {
            case InputMode.Hold:
                latched = held && !blockUntilRelease;
                break;

            case InputMode.Toggle:
                if (PressedThisFrame) latched = !latched;
                break;

            case InputMode.Hybrid:
                if (PressedThisFrame)
                {
                    if (latched) { latched = false; blockUntilRelease = true; }
                    else { latched = true; pressTime = Time.unscaledTime; }
                }
                else if (released && latched && Time.unscaledTime - pressTime > hybridThreshold)
                {
                    latched = false;
                }
                break;
        }

        Active = latched;
    }

    // Something took the action away (reload, sprint, swap). A still-held button
    // resumes it afterwards; a latched toggle does not.
    public void Clear()
    {
        if (!held) latched = false;
        Active = latched;
    }

    // Something forced the action off even while held (e.g. sprint stands you up).
    // Held buttons must be released and pressed again.
    public void ForceOff()
    {
        latched = false;
        blockUntilRelease = held;
        Active = false;
    }
}
