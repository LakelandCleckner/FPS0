using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Delivery;
using Combat.Sources;

public class HandgunFire : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] AudioClip gunFireClip;
    [SerializeField] AudioClip emptyGunClip;
    private PlayerAudio playerAudio;

    [Header("Fire Settings")]
    [SerializeField] float roundsPerMinute = 300f;
    private float fireDelay;
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
        fireDelay = 60f / roundsPerMinute;
        playerAudio = GetComponentInParent<PlayerAudio>();

        fireStrategy = fireStrategyBehaviour as IFireStrategy;
        if (fireStrategy == null)
            Debug.LogError("[HandgunFire] Assigned Fire Strategy doesn't implement IFireStrategy.");
    }

    void Update()
    {
        if (Mouse.current.leftButton.isPressed && Time.time >= nextFireTime)
        {
            Shoot();
            nextFireTime = Time.time + fireDelay;
        }
    }

    void Shoot()
    {
        if (GlobalAmmo.handgunAmmoCount <= 0)
        {
            playerAudio.Play3D(emptyGunClip);
            return;
        }

        GlobalAmmo.handgunAmmoCount -= 1;
        playerAudio.Play3D(gunFireClip);

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