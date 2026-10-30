using UnityEngine;
using Combat.Loadout;

namespace Combat.Weapons
{
    // Ammo pickup: refills a shared pool if one is set, otherwise the held weapon's own reserves.
    public class AmmoPickup : MonoBehaviour
    {
        public enum AmountUnit { Rounds, Magazines }

        [SerializeField] private AudioClip ammoCollectClip;

        [Tooltip("Pool to refill. Empty = the held weapon's own reserves.")]
        [SerializeField] private AmmoPoolSO pool;

        [SerializeField] private int amount = 10;

        [Tooltip("Held-weapon fallback only: Magazines uses that weapon's mag size. Pools always take rounds.")]
        [SerializeField] private AmountUnit unit = AmountUnit.Rounds;

        [Tooltip("Take the pickup even when nothing could use it (empty-handed, infinite reserves, pool full).")]
        [SerializeField] private bool consumeWhenUnused = false;

        private bool taken;

        // Stay as well as Enter so an unused pickup still works after a swap while standing on it.
        private void OnTriggerEnter(Collider other) => TryCollect(other);
        private void OnTriggerStay(Collider other) => TryCollect(other);

        private void TryCollect(Collider other)
        {
            if (taken || !other.CompareTag("Player")) return;

            bool used = pool != null ? FillPool(other) : FillHeld(other);
            if (!used && !consumeWhenUnused) return;

            taken = true;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            var playerAudio = Find<PlayerAudio>(other);
            if (playerAudio != null && ammoCollectClip != null)
                playerAudio.Play2D(ammoCollectClip);

            Destroy(gameObject);
        }

        private bool FillPool(Collider other)
        {
            var inventory = Find<PlayerInventory>(other);
            if (inventory == null) return false;

            int before = inventory.GetAmmo(pool);
            inventory.AddAmmo(pool, amount);
            return inventory.GetAmmo(pool) > before;
        }

        private bool FillHeld(Collider other)
        {
            var loadout = Find<WeaponLoadout>(other);
            var held = loadout != null ? loadout.Active : null;
            var ammo = held != null ? held.GetComponent<WeaponAmmo>() : null;
            if (ammo == null || ammo.InfiniteReserves) return false;

            int rounds = unit == AmountUnit.Magazines ? amount * ammo.MagSize : amount;
            int before = ammo.Reserves;
            ammo.AddReserves(rounds);
            return ammo.Reserves > before;
        }

        private static T Find<T>(Collider other) where T : Component
        {
            var c = other.GetComponentInParent<T>();
            return c != null ? c : other.GetComponentInChildren<T>();
        }
    }
}