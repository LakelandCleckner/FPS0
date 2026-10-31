using UnityEngine;
using Combat.Core;

// Short pitch/roll kick away from the hit direction, layered on top of MouseLook after it writes rotation.
public class CameraBump : MonoBehaviour
{
    [SerializeField] private PlayerHurtbox hurtbox;
    [SerializeField] private Transform cameraTransform;

    [Header("Kick")]
    [SerializeField] private float rollDegrees = 4f;
    [SerializeField] private float pitchDegrees = 2.5f;
    [Tooltip("Damage that gives a full-size kick; smaller hits scale down.")]
    [SerializeField] private float fullKickDamage = 40f;
    [Range(0f, 1f)][SerializeField] private float minKick = 0.5f;

    [Header("Timing")]
    [SerializeField] private float riseTime = 0.04f;
    [SerializeField] private float decayTime = 0.3f;

    private Vector2 kick;     // x = pitch, y = roll
    private float kickTime = -1f;
    private Quaternion applied = Quaternion.identity;   // offset written last frame
    private Quaternion written;                          // rotation we left on the camera
    private bool hasWritten;

    private void Awake()
    {
        if (hurtbox == null) hurtbox = GetComponentInParent<PlayerHurtbox>();
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam != null) cameraTransform = cam.transform;
        }
    }

    private void OnEnable()
    {
        if (hurtbox != null) hurtbox.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        if (hurtbox != null) hurtbox.Damaged -= OnDamaged;
    }

    private void OnDamaged(Transform attacker, Vector3 from, float amount)
    {
        if (cameraTransform == null) return;

        Vector3 to = from - cameraTransform.position; to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;
        to.Normalize();

        Vector3 right = cameraTransform.right; right.y = 0f; right.Normalize();
        Vector3 fwd = cameraTransform.forward; fwd.y = 0f; fwd.Normalize();

        float scale = Mathf.Lerp(minKick, 1f, fullKickDamage > 0f ? Mathf.Clamp01(amount / fullKickDamage) : 1f);
        // Hit from the right rolls the view left; hit from the front tips it up.
        kick = new Vector2(-Vector3.Dot(to, fwd) * pitchDegrees, Vector3.Dot(to, right) * rollDegrees) * scale;
        kickTime = Time.time;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null || !hasWritten && kickTime < 0f) return;

        // Strip last frame's offset if nothing else rewrote the rotation, so the kick never accumulates.
        Quaternion baseRot = cameraTransform.localRotation;
        if (hasWritten && baseRot == written)
            baseRot = baseRot * Quaternion.Inverse(applied);

        Quaternion offset = Quaternion.identity;
        if (kickTime >= 0f)
        {
            float age = Time.time - kickTime;
            if (age >= riseTime + decayTime) kickTime = -1f;
            else
            {
                float w = age < riseTime
                    ? age / Mathf.Max(0.0001f, riseTime)
                    : 1f - Mathf.SmoothStep(0f, 1f, (age - riseTime) / Mathf.Max(0.0001f, decayTime));
                offset = Quaternion.Euler(kick.x * w, 0f, kick.y * w);
            }
        }

        cameraTransform.localRotation = baseRot * offset;
        applied = offset;
        written = cameraTransform.localRotation;
        hasWritten = kickTime >= 0f;
    }
}