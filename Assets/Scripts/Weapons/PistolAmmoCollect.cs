using UnityEngine;

public class PistolAmmoCollect : MonoBehaviour
{
    [SerializeField] private AudioClip ammoCollectClip;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        GetComponent<BoxCollider>().enabled = false;

        PlayerAudio playerAudio = other.GetComponent<PlayerAudio>();
        if (playerAudio != null)
        {
            playerAudio.Play2D(ammoCollectClip);
        }

        GlobalAmmo.handgunAmmoCount += 10;

        Destroy(gameObject);
    }
}