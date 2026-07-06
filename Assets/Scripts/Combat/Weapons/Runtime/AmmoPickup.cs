using UnityEngine;

namespace Combat.Weapons
{
    // Ammo pickup — now feeds a weapon's WeaponAmmo reserves instead of the old
    // static GlobalAmmo. Finds the player's WeaponAmmo on trigger.
    // (Replaces PistolAmmoCollect. Kept generic — amount is configurable.)
    public class AmmoPickup : MonoBehaviour
    {
        [SerializeField] private AudioClip ammoCollectClip;
        [SerializeField] private int amount = 10;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            // find the player's weapon ammo (adjust the search to your hierarchy)
            var ammo = other.GetComponentInChildren<WeaponAmmo>();
            if (ammo != null)
                ammo.AddReserves(amount);

            GetComponent<BoxCollider>().enabled = false;

            var playerAudio = other.GetComponent<PlayerAudio>();
            if (playerAudio != null && ammoCollectClip != null)
                playerAudio.Play2D(ammoCollectClip);

            Destroy(gameObject);
        }
    }
}
