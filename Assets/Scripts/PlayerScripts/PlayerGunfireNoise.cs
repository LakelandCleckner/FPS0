using UnityEngine;
using GOAPGettingStarted.Behaviours;

// On the player: every shot from the held weapon is a noise enemies can hear
public class PlayerGunfireNoise : MonoBehaviour
{
    [Tooltip("Found on this object if empty.")]
    [SerializeField] private PlayerPresentation presentation;

    private void Awake()
    {
        if (presentation == null) presentation = GetComponent<PlayerPresentation>();
    }

    private void OnEnable()
    {
        if (presentation != null) presentation.Fired += HandleFired;
    }

    private void OnDisable()
    {
        if (presentation != null) presentation.Fired -= HandleFired;
    }

    private void HandleFired(float interval) => AINoise.Emit(transform.position, transform);
}
