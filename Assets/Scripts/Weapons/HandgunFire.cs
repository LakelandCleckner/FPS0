using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class HandgunFire : MonoBehaviour
{

    [SerializeField] AudioSource gunFire;
    [SerializeField] GameObject handgun;
    [SerializeField] bool canFire = true;
    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (canFire == true)
            {
                canFire = false;
                StartCoroutine(FiringGun());
            }
        }
    }

    IEnumerator FiringGun()
    {
        gunFire.Play();
        handgun.GetComponent<Animator>().Play("HandgunFire");
        yield return new WaitForSeconds(0.42857f);
        handgun.GetComponent<Animator>().Play("New State");
        canFire = true;
    }
}
