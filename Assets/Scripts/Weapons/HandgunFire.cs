using UnityEngine;
using UnityEngine.InputSystem;

public class HandgunFire : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] AudioClip gunFireClip;
    [SerializeField] AudioClip emptyGunClip;
    [SerializeField] AudioClip hitmarkerClip;
    private PlayerAudio playerAudio;

    [Header("Kill Feedback")]
    [SerializeField] private AudioClip killClip;

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
    [SerializeField] private HitmarkerUI hitmarkerUI;


    void Start()
    {
        fireDelay = 60f / roundsPerMinute; // calculate delay between shots
        playerAudio = GetComponentInParent<PlayerAudio>();
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
            playerAudio.Play3D(emptyGunClip);
            return;
        }

        GlobalAmmo.handgunAmmoCount -= 1;
        playerAudio.Play3D(gunFireClip);
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

                bool isHeadshot = hitbox.bodyPart == BodyPart.Head;
                bool isKill = hitbox.enemyHealth.CurrentHealth <= 0f;

                //Hitmarker Sound
                if (playerAudio != null && hitmarkerClip != null)
                {
                    float pitch = isHeadshot ? 1.25f : 1f;  // higher pitch for crits
                    float volume = isHeadshot ? 1f : 0.5f;  // quieter normal hit
                    playerAudio.Play2D(hitmarkerClip, volume, pitch);
                }


                //Hitmarker Visual
                if (hitmarkerUI != null)
                {
                    Color color = isHeadshot ? Color.red : Color.white;
                    if (isKill)
                        color = Color.green;
                    hitmarkerUI.ShowHitmarker(color);
                }

                //Kill Audio
                if (isKill && killClip != null)
                {
                    playerAudio.Play2D(killClip);
                }
            }
        }
    }
}