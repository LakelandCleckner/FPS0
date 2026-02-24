using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class HandgunFire : MonoBehaviour
{
    [SerializeField] AudioSource gunFire;
    [SerializeField] GameObject handgun;
    [SerializeField] GameObject crosshair;
    [SerializeField] bool canFire = true;
    [SerializeField] AudioSource emptyGunSound;

    [Header("Fire Settings")]
    [SerializeField] float roundsPerMinute = 300f;

    [Header("Weapon Stats")]
    [SerializeField] float baseDamage = 20f;
    [SerializeField] float range = 100f;

    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (canFire)
            {
                if (GlobalAmmo.handgunAmmoCount == 0)
                {
                    canFire = false;
                    StartCoroutine(EmptyGun());
                }
                else
                {
                    canFire = false;
                    StartCoroutine(FiringGun());
                }
            }
        }
    }

    void RaycastShoot()
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        Debug.DrawRay(ray.origin, ray.direction * range, Color.red, 1f);

        if (Physics.Raycast(ray, out hit, range))
        {
            Debug.Log("Hit object: " + hit.collider.name);

            EnemyHitbox hitbox = hit.collider.GetComponentInParent<EnemyHitbox>();

            if (hitbox != null)
            {
                Debug.Log("Hit body part: " + hitbox.bodyPart);
                hitbox.ApplyDamage(baseDamage);
            }
            else
            {
                Debug.Log("No EnemyHitbox found.");
            }
        }
    }

    IEnumerator FiringGun()
    {
        gunFire.Play();
        GlobalAmmo.handgunAmmoCount -= 1;

        RaycastShoot();

        handgun.GetComponent<Animator>().Play("HandgunFire");
        crosshair.GetComponent<Animator>().Play("HandgunFireCrosshair");

        float delay = 60f / roundsPerMinute;
        yield return new WaitForSeconds(delay);

        handgun.GetComponent<Animator>().Play("New State");
        crosshair.GetComponent<Animator>().Play("New State");

        canFire = true;
    }

    IEnumerator EmptyGun()
    {
        emptyGunSound.Play();
        yield return new WaitForSeconds(0.6f);
        canFire = true;
    }
}