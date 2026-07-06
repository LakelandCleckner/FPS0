using UnityEngine;
using Combat.Sources;
using Combat.Weapons;

namespace Combat.Weapons
{
    // Per-weapon AMMO RUNTIME STATE (not on an SO — this is live, mutable state).
    // Owns the magazine, reserves, and a small reload STATE MACHINE.
    //
    // Reload is a state machine (not a bare timer) so it cleanly supports:
    //  - cancel-by-fire now (and other cancel triggers later)
    //  - a CONFIGURABLE refill point within the reload (line up with animation)
    //  - a future TWO-STAGE reload (ammo refilled but a chamber/cock step before
    //    the gun can fire) — add a state after refill.
    //
    // Magazine SIZE and reload TIME are stats (resolved from the weapon). Rounds
    // LOADED and reserves are runtime state here.
    public class WeaponAmmo : MonoBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private WeaponDamageSource damageSource;

        [Header("Reload")]
        [Tooltip("Normalized point in the reload (0..1) when the mag actually refills. " +
                 "1 = at the end (default). Lower to line up with an animation.")]
        [Range(0f, 1f)]
        [SerializeField] private float refillPoint = 1f;
        [Tooltip("Auto-start a reload when the mag hits empty.")]
        [SerializeField] private bool autoReloadOnEmpty = false;

        // runtime state
        private int magazine;      // rounds currently loaded
        private int reserves;      // rounds outside the mag
        private bool infiniteReserves;
        private int magSize;       // resolved cap

        // reload state machine
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

            var stats = damageSource.GetStats();
            magSize = Mathf.Max(1, Mathf.RoundToInt(stats.MagazineSize));
            infiniteReserves = weapon.ResolveInfiniteReserves();

            magazine = magSize;                 // start full
            reserves = weapon.startingReserves; // starting reserves
        }

        // Try to consume one round for a shot. Returns true if a round was spent.
        public bool TryConsume()
        {
            // Empty: don't fire, and don't cancel an in-progress (auto)reload —
            // clicking on empty should let the reload finish, not restart it.
            if (magazine <= 0)
            {
                if (autoReloadOnEmpty && state != ReloadState.Reloading)
                    BeginReload();
                return false;
            }

            // We have a round: an intentional fire cancels a reload in progress
            // (so you can shoot mid-reload), then spends the round.
            if (state == ReloadState.Reloading)
                CancelReload();

            magazine--;
            if (magazine <= 0 && autoReloadOnEmpty)
                BeginReload();
            return true;
        }

        // Start a reload if it makes sense to. Returns TRUE only if a reload
        // actually began — so feedback (sound/animation) only plays on a real
        // reload, not on a rejected one (full mag, already reloading, no reserves).
        public bool BeginReload()
        {
            if (state == ReloadState.Reloading) return false;    // already reloading
            if (magazine >= magSize) return false;               // already full
            if (!infiniteReserves && reserves <= 0) return false;// nothing to load

            var stats = damageSource.GetStats();
            reloadDuration = Mathf.Max(0.01f, stats.ReloadTime);
            reloadElapsed = 0f;
            refilledThisReload = false;
            state = ReloadState.Reloading;
            return true;
        }

        // Cancel-by-fire (and pluggable for other cancel triggers later).
        public void CancelReload()
        {
            if (state != ReloadState.Reloading) return;
            // if the refill point was already reached, the mag keeps what it got.
            state = ReloadState.Ready;
        }

        private void Update()
        {
            if (state != ReloadState.Reloading) return;

            reloadElapsed += Time.deltaTime;

            // refill at the configurable normalized point
            float t = reloadElapsed / reloadDuration;
            if (!refilledThisReload && t >= refillPoint)
            {
                DoRefill();
                refilledThisReload = true;
            }

            if (reloadElapsed >= reloadDuration)
            {
                if (!refilledThisReload) DoRefill(); // safety if refillPoint==1
                state = ReloadState.Ready;
            }
        }

        // Top up only what the mag needs, pulling from reserves (unless infinite).
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

        // Pickups feed reserves.
        public void AddReserves(int amount)
        {
            if (infiniteReserves) return; // no point
            reserves = Mathf.Max(0, reserves + amount);
        }
    }
}