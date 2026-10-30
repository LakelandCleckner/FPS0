using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Loadout;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Perks
{
    // Runs a weapon's perks. Lives on the weapon runtime prefab: enabled while the weapon is in the
    // loadout, so perk state resets when it leaves the loadout, not when it's stowed.
    public class WeaponPerks : MonoBehaviour
    {
        [SerializeField] private bool debugLog = false;

        private WeaponDamageSource source;
        private WeaponFireController controller;
        private WeaponLoadout loadout;
        private readonly List<PerkRuntime> runtimes = new List<PerkRuntime>();
        private PerkContext ctx;
        private bool started;

        public IReadOnlyList<PerkRuntime> Runtimes => runtimes;

        private void Awake()
        {
            controller = GetComponent<WeaponFireController>();
            source = controller != null ? controller.DamageSource : GetComponent<WeaponDamageSource>();
        }

        // Start, so the weapon's own Awake (stat container, effects) has run first.
        private void Start()
        {
            started = true;
            Attach();
        }

        private void OnEnable()
        {
            if (started) Attach();
        }

        private void OnDisable() => Detach();

        // Rebuilds every perk, e.g. after a reroll.
        public void Reattach()
        {
            Detach();
            if (isActiveAndEnabled) Attach();
        }

        private void Attach()
        {
            if (runtimes.Count > 0 || source == null) return;

            var instance = source.Instance;
            if (instance == null) return;

            loadout = GetComponentInParent<WeaponLoadout>();
            var player = source.Attacker as CombatantStats;

            ctx = new PerkContext
            {
                Item = instance,
                Weapon = source,
                Ammo = GetComponent<WeaponAmmo>(),
                Bus = WeaponEventBus.FindFor(this),
                Applier = GetComponentInParent<LoadoutModifierApplier>(),
                Clock = PerkClock.For(this),
                Loadout = loadout,
                Player = player,
                Health = player != null ? player.GetComponent<CombatantHealth>() : null,
                IsHeld = IsHeldNow()
            };

            if (ctx.Applier == null) Debug.LogWarning($"[WeaponPerks] '{name}' has no LoadoutModifierApplier above it; stat perks do nothing.");

            foreach (var perk in instance.AllPerks())
            {
                if (perk == null) continue;
                var rt = perk.CreateRuntime();
                if (rt == null) continue;

                try { rt.Attach(perk, ctx); runtimes.Add(rt); }
                catch (System.Exception e) { Debug.LogError($"[WeaponPerks] {perk.name} failed to attach: {e}"); }

                if (debugLog) Debug.Log($"[WeaponPerks] {name} + {perk.name}");
            }

            if (loadout != null)
            {
                loadout.OnWeaponEquipped += HandleEquipped;
                loadout.OnStowStarted += HandleStowStarted;
            }
        }

        private void Detach()
        {
            if (loadout != null)
            {
                loadout.OnWeaponEquipped -= HandleEquipped;
                loadout.OnStowStarted -= HandleStowStarted;
            }

            for (int i = runtimes.Count - 1; i >= 0; i--) runtimes[i].Detach();
            runtimes.Clear();
        }

        // Held = equip finished until stow starts, the same window as InHands modifiers.
        private bool IsHeldNow()
            => loadout != null && controller != null && loadout.Active == controller && !loadout.IsSwapping;

        private void HandleEquipped(WeaponFireController c) => SetHeld(c != null && c == controller);

        private void HandleStowStarted(WeaponFireController c, float duration)
        {
            if (c == controller) SetHeld(false);
        }

        private void SetHeld(bool held)
        {
            if (ctx == null || ctx.IsHeld == held) return;
            ctx.IsHeld = held;
            for (int i = 0; i < runtimes.Count; i++) runtimes[i].NotifyHeld(held);
        }
    }
}
