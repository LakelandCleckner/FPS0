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
    [SerializeField] private HitscanStrategy fireStrategy;
    [SerializeField] private WeaponDamageSource damageSource;

    void Start()
    {
        fireDelay = 60f / roundsPerMinute;
        playerAudio = GetComponentInParent<PlayerAudio>();
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

        // Delivery + source: strategy reads the source, resolver handles the rest
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