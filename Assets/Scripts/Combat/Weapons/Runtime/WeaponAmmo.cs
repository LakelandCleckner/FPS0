using UnityEngine;
using Combat.Sources;
using Combat.Weapons;

namespace Combat.Weapons
{
    // Per-weapon AMMO RUNTIME STATE. Owns the magazine, reserves, and a small
    // reload STATE MACHINE.
    //
    // Phase 2f: magazine SIZE and reload TIME are read from the weapon's resolved
    // stat accessors (StatContainer) instead of the retired StatBlock.
    public class WeaponAmmo : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private WeaponDamageSource damageSource;

        [Header("Reload")]
        [Range(0f, 1f)]
        [SerializeField] private float refillPoint = 1f;
        [SerializeField] private bool autoReloadOnEmpty = false;

        private int magazine;
        private int reserves;
        private bool infiniteReserves;
        private int magSize;

        private enum ReloadState { Ready, Reloading }
        private ReloadState state = ReloadState.Ready;
        private float reloadElapsed;
        private float reloadDuration;
        private bool refilledThisReload;

        public int Magazine => magazine;
        public int Reserves => reserves;
        public bool IsReloading => state == ReloadState.Reloading;
        public int MagSize => magSize;
        public bool InfiniteReserves => infiniteReserves;

        private void Start()
        {
            var weapon = damageSource != null ? damageSource.Weapon : null;
            if (weapon == null)
            {
                Debug.LogError("[WeaponAmmo] No weapon on damage source.");
                return;
            }

            magSize = Mathf.Max(1, Mathf.RoundToInt(damageSource.ResolvedMagazineSize));
            infiniteReserves = weapon.ResolveInfiniteReserves();

            magazine = magSize;
            reserves = weapon.startingReserves;
        }

        public bool TryConsume()
        {
            if (magazine <= 0)
            {
                if (autoReloadOnEmpty && state != ReloadState.Reloading)
                    BeginReload();
                return false;
            }

            if (state == ReloadState.Reloading)
                CancelReload();

            magazine--;
            if (magazine <= 0 && autoReloadOnEmpty)
                BeginReload();
            return true;
        }

        public bool BeginReload()
        {
            if (state == ReloadState.Reloading) return false;
            if (magazine >= magSize) return false;
            if (!infiniteReserves && reserves <= 0) return false;

            reloadDuration = Mathf.Max(0.01f, damageSource.ResolvedReloadTime);
            reloadElapsed = 0f;
            refilledThisReload = false;
            state = ReloadState.Reloading;
            return true;
        }

        public void CancelReload()
        {
            if (state != ReloadState.Reloading) return;
            state = ReloadState.Ready;
        }

        private void Update()
        {
            if (state != ReloadState.Reloading) return;

            reloadElapsed += Time.deltaTime;

            float t = reloadElapsed / reloadDuration;
            if (!refilledThisReload && t >= refillPoint)
            {
                DoRefill();
                refilledThisReload = true;
            }

            if (reloadElapsed >= reloadDuration)
            {
                if (!refilledThisReload) DoRefill();
                state = ReloadState.Ready;
            }
        }

        private void DoRefill()
        {
            int needed = magSize - magazine;
            if (needed <= 0) return;

            if (infiniteReserves)
            {
                magazine = magSize;
                return;
            }

            int pulled = Mathf.Min(needed, reserves);
            magazine += pulled;
            reserves -= pulled;
        }

        public void AddReserves(int amount)
        {
            if (infiniteReserves) return;
            reserves = Mathf.Max(0, reserves + amount);
        }
    }
}