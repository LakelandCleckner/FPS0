using System;
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
    //
    // LOADOUT: this component keeps running while its weapon is stowed. BeginReload
    // stays ungated so a stowed weapon can be reloaded by a perk; only the AUTOMATIC
    // reload-on-empty is held to the weapon in hand.
    //
    // A RELOAD IS COMMITTED once it starts. Firing cannot cancel it — the Destiny
    // model for magazine-fed weapons. Only two things cancel a reload, and both are
    // the weapon leaving the ready position: stowing it, and starting a sprint.
    // Both call CancelReload from WeaponLoadout.
    //
    // PER-SHELL RELOAD (archetype reloadStyle = PerShell, e.g. shotguns) is the one
    // exception: Start -> one shell per step -> End. Firing requests an interrupt;
    // the current shell finishes, End is skipped, and a handling-driven READY time
    // (equip_time x interruptReadyFraction) runs before the weapon may fire.
    // Sprint/stow cancel still works and keeps the shells already loaded.
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

        // Per-shell reload state. Durations are fixed at BeginReload, from the same
        // resolved reload_time, so a reload perk scales every phase together.
        private enum ShellPhase { Start, Loading, End, Readying }
        private bool shellReload;
        private ShellPhase shellPhase;
        private float phaseElapsed;
        private float shellStartDuration;
        private float shellDuration;
        private float shellEndDuration;
        private float readyDuration;
        private bool interruptRequested;

        // Presentation hooks — direct C# events, same convention as the controller's
        // OnFired. ReloadStarted is the DECISION (fires for auto-reload-on-empty too,
        // which the controller's OnReloadStarted request never announced).
        public event Action ReloadStarted;
        public event Action ReloadCancelled;
        public event Action<float> ReloadInterrupted;   // arg: ready duration

        private WeaponEventBus bus;

        // Set by WeaponLoadout. Gates the CONVENIENCE auto-reload only — a weapon you
        // aren't holding shouldn't quietly reload itself the instant it empties.
        //
        // It deliberately does NOT gate BeginReload: an Auto-Loading-Holster-style
        // perk reloads a weapon *because* it is stowed, and gating the state machine
        // would make that impossible to express. Perks call BeginReload directly.
        public bool IsHeld { get; set; } = true;

        // Exposed so a subscriber can filter bus events to THIS weapon (per-weapon
        // routing, doc 07 §2) without needing a separate inspector reference.
        public WeaponDamageSource Source => damageSource;

        public int Magazine => magazine;
        public int Reserves => reserves;
        public bool IsReloading => state == ReloadState.Reloading;
        public int MagSize => magSize;
        public bool InfiniteReserves => infiniteReserves;

        public bool IsShellReload => state == ReloadState.Reloading && shellReload;
        public float ReloadDuration => reloadDuration;
        public float ShellStartDuration => shellStartDuration;
        public float ShellDuration => shellDuration;
        public float ShellEndDuration => shellEndDuration;

        // True while another shell will follow the one currently loading. Drives the
        // animator's ReloadStep -> ReloadEnd exit. An interrupt does NOT clear this —
        // interrupts skip End and cross-fade out instead.
        public bool ShellWillContinue
        {
            get
            {
                if (!IsShellReload) return false;
                if (shellPhase == ShellPhase.Start) return true;
                if (shellPhase != ShellPhase.Loading) return false;
                return magazine + 1 < magSize && (infiniteReserves || reserves > 1);
            }
        }

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

            magSize = ResolveMagSize();
            infiniteReserves = weapon.ResolveInfiniteReserves();

            magazine = magSize;
            reserves = weapon.startingReserves;

            // Seed subscribers with the opening state so a display doesn't have to
            // poll once before the first real transition.
            Publish(WeaponEventType.AmmoChanged);
        }

        private int ResolveMagSize()
            => damageSource != null
                ? Mathf.Max(1, Mathf.RoundToInt(damageSource.ResolvedMagazineSize))
                : 1;

        // magSize used to be cached once in Start and never re-read, so a runtime
        // magazine_size modifier — armour, a perk, an upgrade — silently did nothing.
        // Re-resolved on access instead. ResolvedMagazineSize is already a cached
        // container read, so this is a version compare in the steady state.
        //
        // On a change, current ammo is left alone and clamped down, matching
        // CombatantHealth's ClampOnly: extra capacity is headroom you reload into,
        // never free rounds.
        private void RefreshMagSize()
        {
            int resolved = ResolveMagSize();
            if (resolved == magSize) return;

            magSize = resolved;

            if (magazine > magSize)
            {
                magazine = magSize;
                Publish(WeaponEventType.AmmoChanged);
            }
        }

        public bool TryConsume()
        {
            RefreshMagSize();

            if (magazine <= 0)
            {
                if (autoReloadOnEmpty && IsHeld && state != ReloadState.Reloading)
                    BeginReload();
                return false;
            }

            // NOTE: firing during a reload used to CancelReload() here. It doesn't any
            // more — a reload is committed. The controller refuses to fire while
            // IsReloading, so this is never reached mid-reload; if something ever
            // bypassed that gate, silently eating the reload would be the wrong answer.

            magazine--;
            Publish(WeaponEventType.AmmoChanged);

            if (magazine <= 0)
            {
                Publish(WeaponEventType.MagEmpty);
                if (autoReloadOnEmpty && IsHeld)
                    BeginReload();
            }
            return true;
        }

        public bool BeginReload()
        {
            RefreshMagSize();

            if (state == ReloadState.Reloading) return false;
            if (magazine >= magSize) return false;
            if (!infiniteReserves && reserves <= 0) return false;

            reloadDuration = Mathf.Max(0.01f, damageSource.ResolvedReloadTime);
            reloadElapsed = 0f;
            refilledThisReload = false;
            state = ReloadState.Reloading;

            var archetype = damageSource.Weapon != null ? damageSource.Weapon.archetype : null;
            shellReload = archetype != null && archetype.reloadStyle == ReloadStyle.PerShell;
            if (shellReload) SetupShellReload(archetype);

            Publish(WeaponEventType.ReloadStart);
            ReloadStarted?.Invoke();
            return true;
        }

        // Called on stow and on sprint start — the two cases where the weapon leaves
        // the ready position. Firing does NOT call this.
        public void CancelReload()
        {
            if (state != ReloadState.Reloading) return;
            state = ReloadState.Ready;
            interruptRequested = false;
            ReloadCancelled?.Invoke();

            // Not in doc 07's table, but this is a real transition perks care about
            // (Destiny-style "cancel the reload to keep the buff" play patterns) and
            // it costs nothing to expose now.
            Publish(WeaponEventType.ReloadCancelled);
        }

        // Fire input during a per-shell reload. Honoured after the current shell.
        // Ignored once End or Ready is already running.
        public void RequestInterrupt()
        {
            if (!IsShellReload) return;
            if (shellPhase == ShellPhase.Start || shellPhase == ShellPhase.Loading)
                interruptRequested = true;
        }

        private void SetupShellReload(WeaponArchetypeSO a)
        {
            // reload_time = a FULL reload from empty. Start and End are fractions of
            // it; the remainder is split evenly across the magazine, so a partial
            // reload is naturally shorter.
            float startF = Mathf.Clamp01(a.shellStartFraction);
            float endF = Mathf.Clamp01(a.shellEndFraction);
            float loadF = Mathf.Max(0.01f, 1f - startF - endF);

            shellStartDuration = reloadDuration * startF;
            shellEndDuration = reloadDuration * endF;
            shellDuration = reloadDuration * loadF / Mathf.Max(1, magSize);
            readyDuration = Mathf.Max(0f, damageSource.ResolvedEquipTime * a.interruptReadyFraction);

            shellPhase = ShellPhase.Start;
            phaseElapsed = 0f;
            interruptRequested = false;
        }

        private void TickShellReload()
        {
            phaseElapsed += Time.deltaTime;

            switch (shellPhase)
            {
                case ShellPhase.Start:
                    if (phaseElapsed < shellStartDuration) return;
                    phaseElapsed -= shellStartDuration;
                    shellPhase = ShellPhase.Loading;
                    return;

                case ShellPhase.Loading:
                    if (phaseElapsed < shellDuration) return;
                    phaseElapsed -= shellDuration;
                    InsertShell();

                    if (!CanLoadMore())
                    {
                        shellPhase = ShellPhase.End;
                    }
                    else if (interruptRequested)
                    {
                        shellPhase = ShellPhase.Readying;
                        phaseElapsed = 0f;
                        ReloadInterrupted?.Invoke(readyDuration);
                    }
                    return;

                case ShellPhase.End:
                    if (phaseElapsed >= shellEndDuration) CompleteReload();
                    return;

                case ShellPhase.Readying:
                    if (phaseElapsed >= readyDuration) CompleteReload();
                    return;
            }
        }

        private bool CanLoadMore()
        {
            RefreshMagSize();
            return magazine < magSize && (infiniteReserves || reserves > 0);
        }

        private void InsertShell()
        {
            if (!CanLoadMore()) return;

            magazine++;
            if (!infiniteReserves) reserves--;

            Publish(WeaponEventType.AmmoChanged);
            if (magazine >= magSize) Publish(WeaponEventType.MagFull);
        }

        private void CompleteReload()
        {
            state = ReloadState.Ready;
            interruptRequested = false;
            Publish(WeaponEventType.ReloadComplete);
        }

        private void Update()
        {
            if (state != ReloadState.Reloading) return;

            if (shellReload)
            {
                TickShellReload();
                return;
            }

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

                // Doc 03 §8 claims the animator is driven by "fire, reload start,
                // reload end", but no reload-end event existed anywhere — the state
                // machine completed here and told nobody. This is that event.
                Publish(WeaponEventType.ReloadComplete);
            }
        }

        private void DoRefill()
        {
            RefreshMagSize();

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