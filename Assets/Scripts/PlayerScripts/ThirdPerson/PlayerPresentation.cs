using System;
using UnityEngine;
using Combat.Weapons;

// Per-frame snapshot the body reads; the state to replicate later
public struct PresentationState
{
    public Vector2 Move;          // local x/z: 1 = walk, 2 = sprint
    public bool Grounded;
    public float VerticalSpeed;
    public float Crouch;          // 0 standing .. 1 crouched
    public float Pitch;           // degrees, positive looks down
    public bool Aiming;
    public float AimBlend;
    public WeaponSO HeldWeapon;   // null = empty-handed
    public bool Reloading;
    public bool ShellContinue;    // another shell follows the one loading
}

// Timing of a reload as it starts, for presentation to fill
public struct ReloadInfo
{
    public bool Shell;
    public float Duration;        // magazine reload
    public float ShellStart;
    public float ShellStep;       // one shell
    public float ShellEnd;
}

// Samples local player systems after they update and relays weapon events
[DefaultExecutionOrder(100)]
public class PlayerPresentation : MonoBehaviour
{
    [Header("Refs (found automatically if empty)")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerCrouch crouch;
    [SerializeField] private PlayerAim aim;
    [SerializeField] private MouseLook look;
    [SerializeField] private WeaponLoadout loadout;

    // Weapon starting to come up, with the equip duration
    public event Action<WeaponSO, float> EquipStarted;
    // Held weapon starting to go down, with the stow duration
    public event Action<WeaponSO, float> StowStarted;
    // Held weapon settled (startup and end of each equip); null = empty-handed
    public event Action<WeaponSO> WeaponEquipped;
    // One shot, with the shot interval in seconds
    public event Action<float> Fired;
    // Held weapon's reload began / was cancelled / was interrupted (arg: ready time)
    public event Action<ReloadInfo> ReloadStarted;
    public event Action ReloadCancelled;
    public event Action<float> ReloadInterrupted;

    public PresentationState State { get; private set; }

    private WeaponFireController firing;
    private WeaponAmmo ammo;

    private void Awake()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (crouch == null) crouch = GetComponent<PlayerCrouch>();
        if (aim == null) aim = GetComponent<PlayerAim>();
        if (look == null) look = GetComponentInChildren<MouseLook>();
        if (loadout == null) loadout = GetComponentInChildren<WeaponLoadout>(true);
    }

    private void OnEnable()
    {
        if (loadout == null) return;
        loadout.OnEquipStarted += HandleEquipStarted;
        loadout.OnStowStarted += HandleStowStarted;
        loadout.OnWeaponEquipped += HandleWeaponEquipped;
    }

    private void OnDisable()
    {
        if (loadout != null)
        {
            loadout.OnEquipStarted -= HandleEquipStarted;
            loadout.OnStowStarted -= HandleStowStarted;
            loadout.OnWeaponEquipped -= HandleWeaponEquipped;
        }
        Track(null);
    }

    private void Update()
    {
        var s = new PresentationState();

        if (movement != null)
        {
            s.Move = NormalizedMove(movement.LocalVelocity);
            s.Grounded = movement.IsGrounded;
            s.VerticalSpeed = movement.VerticalVelocity;
        }
        if (crouch != null) s.Crouch = crouch.Blend;
        if (aim != null)
        {
            s.Aiming = aim.IsAiming;
            s.AimBlend = aim.Blend;
        }
        if (look != null) s.Pitch = look.Pitch;

        var active = loadout != null ? loadout.Active : null;
        Track(active);
        s.HeldWeapon = WeaponOf(active);
        s.Reloading = ammo != null && ammo.IsReloading;
        s.ShellContinue = ammo != null && ammo.ShellWillContinue;

        State = s;
    }

    // Same scale as the arms Speed param, kept on the move direction
    private Vector2 NormalizedMove(Vector3 local)
    {
        var flat = new Vector2(local.x, local.z);
        float speed = flat.magnitude;
        if (speed < 0.01f) return Vector2.zero;

        float walk = Mathf.Max(0.01f, movement.WalkSpeed);
        float sprintRange = Mathf.Max(0.01f, walk * (movement.SprintMultiplier - 1f));
        float value = speed <= walk ? speed / walk : 1f + (speed - walk) / sprintRange;

        return flat / speed * Mathf.Clamp(value, 0f, 2f);
    }

    // Follows the held weapon so only its shots and reloads are relayed
    private void Track(WeaponFireController c)
    {
        if (c == firing) return;

        if (firing != null) firing.OnFired -= HandleFired;
        if (ammo != null)
        {
            ammo.ReloadStarted -= HandleReloadStarted;
            ammo.ReloadCancelled -= HandleReloadCancelled;
            ammo.ReloadInterrupted -= HandleReloadInterrupted;
        }

        firing = c;
        ammo = c != null ? c.GetComponent<WeaponAmmo>() : null;

        if (firing != null) firing.OnFired += HandleFired;
        if (ammo != null)
        {
            ammo.ReloadStarted += HandleReloadStarted;
            ammo.ReloadCancelled += HandleReloadCancelled;
            ammo.ReloadInterrupted += HandleReloadInterrupted;
        }
    }

    private void HandleReloadStarted()
    {
        ReloadStarted?.Invoke(new ReloadInfo
        {
            Shell = ammo.IsShellReload,
            Duration = ammo.ReloadDuration,
            ShellStart = ammo.ShellStartDuration,
            ShellStep = ammo.ShellDuration,
            ShellEnd = ammo.ShellEndDuration
        });
    }

    private void HandleReloadCancelled() => ReloadCancelled?.Invoke();
    private void HandleReloadInterrupted(float ready) => ReloadInterrupted?.Invoke(ready);

    private void HandleFired()
    {
        var source = firing != null ? firing.DamageSource : null;
        float rpm = source != null ? source.ResolvedRPM : 0f;
        Fired?.Invoke(rpm > 0f ? 60f / rpm : 0.2f);
    }

    private void HandleEquipStarted(WeaponFireController c, float duration) => EquipStarted?.Invoke(WeaponOf(c), duration);
    private void HandleStowStarted(WeaponFireController c, float duration) => StowStarted?.Invoke(WeaponOf(c), duration);
    private void HandleWeaponEquipped(WeaponFireController c) => WeaponEquipped?.Invoke(WeaponOf(c));

    private static WeaponSO WeaponOf(WeaponFireController c)
    {
        var source = c != null ? c.DamageSource : null;
        return source != null ? source.Weapon : null;
    }
}
