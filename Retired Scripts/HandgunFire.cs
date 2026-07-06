using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Delivery;
using Combat.Sources;

// PHASE 1a NOTE: lightly updated to read RPM and audio from the resolved weapon
// (WeaponDamageSource -> WeaponSO) instead of its own serialized fields. Firing
// and ammo are otherwise UNCHANGED, the reload system and full generalization
// (renaming to a proper WeaponFireController, removing GlobalAmmo) come later.
public class HandgunFire : MonoBehaviour
{
    private PlayerAudio playerAudio;

    [Header("Fire Settings")]
    [Tooltip("Fallback RPM if the weapon source isn't resolved. Real RPM now comes " +
             "from the weapon's resolved stats.")]
    [SerializeField] float fallbackRoundsPerMinute = 300f;
    private float nextFireTime = 0f;

    [Header("References")]
    [SerializeField] GameObject handgun;
    [SerializeField] GameObject crosshair;

    [Header("Combat System")]
    [Tooltip("Drag a component that implements IFireStrategy (HitscanStrategy or ProjectileStrategy).")]
    [SerializeField] private MonoBehaviour fireStrategyBehaviour;
    [SerializeField] private WeaponDamageSource damageSource;

    private IFireStrategy fireStrategy;

    void Start()
    {
        playerAudio = GetComponentInParent<PlayerAudio>();

        fireStrategy = fireStrategyBehaviour as IFireStrategy;
        if (fireStrategy == null)
            Debug.LogError("[HandgunFire] Assigned Fire Strategy doesn't implement IFireStrategy.");
    }

    // RPM now resolved from the weapon; fall back if unavailable.
    private float FireDelay()
    {
        float rpm = (damageSource != null && damageSource.ResolvedRPM > 0f)
            ? damageSource.ResolvedRPM
            : fallbackRoundsPerMinute;
        return 60f / rpm;
    }

    void Update()
    {
        if (Mouse.current.leftButton.isPressed && Time.time >= nextFireTime)
        {
            Shoot();
            nextFireTime = Time.time + FireDelay();
        }
    }

    void Shoot()
    {
        if (GlobalAmmo.handgunAmmoCount <= 0)
        {
            var empty = damageSource != null ? damageSource.EmptyClip : null;
            if (empty != null) playerAudio.Play3D(empty);
            return;
        }

        GlobalAmmo.handgunAmmoCount -= 1;

        var fire = damageSource != null ? damageSource.FireClip : null;
        if (fire != null) playerAudio.Play3D(fire);

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        fireStrategy.Fire(ray.origin, ray.direction, damageSource);

        handgun.GetComponent<Animator>().Play("HandgunFire");
        crosshair.GetComponent<Animator>().Play("HandgunFireCrosshair");
        Invoke("ResetAnimations", 0.1f);
    }

    void ResetAnimations()
    {
        handgun.GetComponent<Animator>().Play("New State");
        crosshair.GetComponent<Animator>().Play("New State");
    }
}