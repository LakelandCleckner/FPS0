using System;
using UnityEngine;
using UnityEngine.Rendering;

// Which body meshes render for this player
public class BodyVisibility : MonoBehaviour
{
    public enum Mode { FirstPerson, ThirdPerson }

    [Tooltip("Full body mesh renderers.")]
    [SerializeField] private Renderer[] fullBody;
    [Tooltip("Legs-only mesh renderers on the same armature.")]
    [SerializeField] private Renderer[] legs;
    [SerializeField] private Mode startMode = Mode.FirstPerson;

    public Mode Current { get; private set; }

    private Renderer[] weapon = Array.Empty<Renderer>();

    private void Awake() => SetMode(startMode);

    public void SetMode(Mode mode)
    {
        Current = mode;
        Apply();
    }

    // Held weapon's world model; follows the full body's shadow mode
    public void SetWeaponRenderers(Renderer[] renderers)
    {
        weapon = renderers ?? Array.Empty<Renderer>();
        Apply();
    }

    private void Apply()
    {
        bool fp = Current == Mode.FirstPerson;
        var bodyMode = fp ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

        if (fullBody != null)
            foreach (var r in fullBody)
                if (r != null) r.shadowCastingMode = bodyMode;

        foreach (var r in weapon)
            if (r != null) r.shadowCastingMode = bodyMode;

        if (legs != null)
            foreach (var r in legs)
            {
                if (r == null) continue;
                r.enabled = fp;
                r.shadowCastingMode = ShadowCastingMode.Off;   // full body already casts
            }
    }
}
