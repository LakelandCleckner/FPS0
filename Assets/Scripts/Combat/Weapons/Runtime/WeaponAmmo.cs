using UnityEngine;
using Combat.Sources;
using Combat.Weapons;
using Combat.Events;

namespace Combat.Weapons
{
    // Per-weapon AMMO RUNTIME STATE. Owns the magazine, reserves, and a small
    // reload STATE MACHINE.
    //
    // Phase 2f: magazine SIZE and reload TIME are read from the weapon's resolved
    // stat accessors (StatContainer) instead of the retired StatBlock.
    //
    // EVENT SURFACE: this component is the authoritative origin for every ammo and
    // reload event (doc 07 §2). It already funnelled every state transition through
    // a named method, so each publish below is a single line at a point that was
    // already the one place that transition could happen — no new state, no new
    // branching. Nothing else may announce these; WeaponFireController's
    // OnReloadStarted is a REQUEST, this is the decision (see WeaponEventBridge).
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

        private WeaponEventBus bus;

        // Exposed so a subscriber can filter bus events to THIS weapon (per-weapon
        // routing, doc 07 §2) without needing a separate inspector reference.
        public WeaponDamageSource Source => damageSource;

        public int Magazine => magazine;
        public int Reserves => reserves;
        public bool IsReloading => state == ReloadState.Reloading;
        public int MagSize => magSize;
        public bool InfiniteReserves => infiniteReserves;

        private void Awake()
        {
            bus = WeaponEventBus.FindFor(this);
        }

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

            // Seed subscribers with the opening state so a display doesn't have to
            // poll once before the first real transition.
            Publish(WeaponEventType.AmmoChanged);
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
            Publish(WeaponEventType.AmmoChanged);

            if (magazine <= 0)
            {
                Publish(WeaponEventType.MagEmpty);
                if (autoReloadOnEmpty)
                    BeginReload();
            }
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

            Publish(WeaponEventType.ReloadStart);
            return true;
        }

        public void CancelReload()
        {
            if (state != ReloadState.Reloading) return;
            state = ReloadState.Ready;

            // Not in doc 07's table, but this is a real transition perks care about
            // (Destiny-style "cancel the reload to keep the buff" play patterns) and
            // it costs nothing to expose now. Note it fires on the interrupt path in
            // TryConsume as well, which is exactly when it matters.
            Publish(WeaponEventType.ReloadCancelled);
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

                // Doc 03 §6 claims the animator is driven by "fire, reload start,
                // reload end", but no reload-end event existed anywhere — the state
                // machine completed here and told nobody. This is that event.
                Publish(WeaponEventType.ReloadComplete);
            }
        }

        private void DoRefill()
        {
            int needed = magSize - magazine;
            if (needed <= 0) return;

            if (infiniteReserves)
            {
                magazine = magSize;
                Publish(WeaponEventType.AmmoChanged);
                Publish(WeaponEventType.MagFull);
                return;
            }

            int pulled = Mathf.Min(needed, reserves);
            magazine += pulled;
            reserves -= pulled;

            Publish(WeaponEventType.AmmoChanged);
            if (magazine >= magSize)
                Publish(WeaponEventType.MagFull);
        }

        public void AddReserves(int amount)
        {
            if (infiniteReserves) return;
            reserves = Mathf.Max(0, reserves + amount);
            Publish(WeaponEventType.AmmoChanged);
        }

        private void Publish(WeaponEventType type)
        {
            if (bus == null) return;
            bus.Publish(WeaponEvent.ForAmmo(type, damageSource, magazine, reserves, magSize));
        }
    }
}