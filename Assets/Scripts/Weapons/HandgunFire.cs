using UnityEngine;
using UnityEngine.InputSystem;

public class HandgunFire : MonoBehaviour
{
    [Header("Audio Clips")]
    [SerializeField] AudioClip gunFireClip;
    [SerializeField] AudioClip emptyGunClip;

    [Header("Fire Settings")]
    [SerializeField] float roundsPerMinute = 300f;
    private float fireDelay;
    private float nextFireTime = 0f;

    [Header("Weapon Stats")]
    [SerializeField] float baseDamage = 20f;
    [SerializeField] float range = 100f;

    [Header("References")]
    [SerializeField] GameObject handgun;
    [SerializeField] GameObject crosshair;


    void Start()
    {
        fireDelay = 60f / roundsPerMinute; // calculate delay between shots
    }

    void Update()
    {
        if (Mouse.current.leftButton.isPressed)
        {
            if (Time.time >= nextFireTime)
            {
                Shoot();
                nextFireTime = Time.time + fireDelay; // schedule next shot
            }
        }
    }

    void Shoot()
    {
        if (GlobalAmmo.handgunAmmoCount <= 0)
        {
            AudioSource.PlayClipAtPoint(emptyGunClip, transform.position);
            return;
        }

        GlobalAmmo.handgunAmmoCount -= 1;
        AudioSource.PlayClipAtPoint(gunFireClip, transform.position);
        RaycastShoot();

        handgun.GetComponent<Animator>().Play("HandgunFire");
        crosshair.GetComponent<Animator>().Play("HandgunFireCrosshair");

        // reset to idle state after a short delay
        Invoke("ResetAnimations", 0.1f);
    }

    void ResetAnimations()
    {
        handgun.GetComponent<Animator>().Play("New State");
        crosshair.GetComponent<Animator>().Play("New State");
    }

    void RaycastShoot()
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        Debug.DrawRay(ray.origin, ray.direction * range, Color.red, 1f);

        if (Physics.Raycast(ray, out hit, range))
        {
            EnemyHitbox hitbox = hit.collider.GetComponentInParent<EnemyHitbox>();
            if (hitbox != null)
            {
                float damage = baseDamage * hitbox.damageMultiplier;
                hitbox.enemyHealth.TakeDamage(damage, hitbox.bodyPart);
            }
        }
    }
}