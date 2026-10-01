using UnityEngine;

// Fades the crosshair out as the player aims, on the same blend that drives the
// zoom and sight alignment. Put it on the crosshair's root (UI object or prefab).
[RequireComponent(typeof(CanvasGroup))]
public class CrosshairAdsFade : MonoBehaviour
{
    [Tooltip("Found in the scene if empty (the crosshair may be spawned at runtime).")]
    [SerializeField] private PlayerAim playerAim;

    [Tooltip("ADS blend at which the crosshair is fully gone.")]
    [Range(0.05f, 1f)] [SerializeField] private float fadeEnd = 0.5f;

    private CanvasGroup group;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (playerAim == null) playerAim = FindFirstObjectByType<PlayerAim>();
    }

    private void LateUpdate()
    {
        float blend = playerAim != null ? playerAim.Blend : 0f;
        group.alpha = 1f - Mathf.Clamp01(blend / fadeEnd);
    }
}
