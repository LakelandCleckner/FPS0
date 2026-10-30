using System.Collections.Generic;
using UnityEngine;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Loadout
{
    // The one place loadout-driven modifiers are registered (armour and weapon perks).
    // Each grant is its own modifier owner, so a refresh removes by owner and re-adds if still live.
    public class LoadoutModifierApplier : MonoBehaviour
    {
        [Tooltip("Found on this object or its children if empty.")]
        [SerializeField] private WeaponLoadout weapons;
        [Tooltip("The player's stats. Found on this object or its parents if empty.")]
        [SerializeField] private CombatantStats player;

        private readonly List<ModifierGrant> grants = new List<ModifierGrant>();
        private readonly HashSet<WeaponDamageSource> bound = new HashSet<WeaponDamageSource>();
        private WeaponDamageSource held;

        private StatContainer PlayerStats => player != null ? player.Container : null;

        private void Awake()
        {
            if (weapons == null) weapons = GetComponentInChildren<WeaponLoadout>();
            if (player == null) player = GetComponentInParent<CombatantStats>();

            if (player == null) Debug.LogWarning("[ModifierApplier] No CombatantStats found; player modifiers are skipped.");
        }

        // Subscribe only; containers may not exist until every Awake has run.
        private void OnEnable()
        {
            if (weapons != null)
            {
                weapons.WeaponBound += HandleBound;
                weapons.WeaponUnbound += HandleUnbound;
                weapons.OnWeaponEquipped += HandleEquipped;
                weapons.OnStowStarted += HandleStowStarted;
            }
        }

        private void OnDisable()
        {
            if (weapons != null)
            {
                weapons.WeaponBound -= HandleBound;
                weapons.WeaponUnbound -= HandleUnbound;
                weapons.OnWeaponEquipped -= HandleEquipped;
                weapons.OnStowStarted -= HandleStowStarted;
            }
        }

        // ------------------------------------------------------------------ public API

        public void Register(ModifierGrant grant)
        {
            if (grant == null || grant.Stat == null) return;
            grants.Add(grant);
            Apply(grant);
        }

        // Changes a registered grant's value and reapplies it.
        public void SetValue(ModifierGrant grant, float value)
        {
            if (grant == null || grant.Value == value) return;
            grant.Value = value;
            if (grants.Contains(grant)) Apply(grant);
        }

        // Removes one grant from every container.
        public void Remove(ModifierGrant grant)
        {
            if (grant == null || !grants.Remove(grant)) return;
            Strip(grant);
        }

        // Removes every grant this owner registered, from every container.
        public void Unregister(object owner)
        {
            if (owner == null) return;

            for (int i = grants.Count - 1; i >= 0; i--)
            {
                var g = grants[i];
                if (!ReferenceEquals(g.Owner, owner)) continue;
                Strip(g);
                grants.RemoveAt(i);
            }
        }

        private void Strip(ModifierGrant g)
        {
            var ps = PlayerStats;
            if (ps != null) ps.RemoveAllFromOwner(g);
            foreach (var w in bound)
            {
                var c = w.SourceStats;
                if (c != null) c.RemoveAllFromOwner(g);
            }
        }

        // ------------------------------------------------------------------ weapons

        private void HandleBound(int slot, WeaponDamageSource weapon)
        {
            if (weapon == null) return;

            bound.Add(weapon);
            RefreshWeapon(weapon, false);
            RefreshPlayer(true);
        }

        private void HandleUnbound(int slot, WeaponDamageSource weapon)
        {
            if (weapon == null) return;

            bound.Remove(weapon);
            if (held == weapon) held = null;

            // Not bound any more, so every grant comes off it.
            RefreshWeapon(weapon, false);
            RefreshPlayer(true);
        }

        private void HandleEquipped(WeaponFireController controller)
        {
            held = controller != null ? controller.DamageSource : null;
            RefreshInHands();
        }

        // InHands grants end when the weapon starts going down, not when it's gone.
        private void HandleStowStarted(WeaponFireController controller, float duration)
        {
            held = null;
            RefreshInHands();
        }

        private void RefreshInHands()
        {
            foreach (var w in bound) RefreshWeapon(w, true);
            RefreshPlayer(true);
        }

        // ------------------------------------------------------------------ apply

        private void Apply(ModifierGrant g)
        {
            if (g.Target == ModifierTarget.Player)
            {
                Refresh(g, PlayerStats, LiveOnPlayer(g));
                return;
            }

            foreach (var w in bound)
                Refresh(g, w.SourceStats, LiveOnWeapon(g, w));
        }

        private void RefreshWeapon(WeaponDamageSource weapon, bool inHandsOnly)
        {
            var c = weapon.SourceStats;
            if (c == null) return;

            for (int i = 0; i < grants.Count; i++)
            {
                var g = grants[i];
                if (g.Target != ModifierTarget.Weapons) continue;
                if (inHandsOnly && g.Scope != ActivityScope.InHands) continue;
                Refresh(g, c, LiveOnWeapon(g, weapon));
            }
        }

        // gatedOnly: just the grants that depend on a weapon's state.
        private void RefreshPlayer(bool gatedOnly)
        {
            var ps = PlayerStats;
            if (ps == null) return;

            for (int i = 0; i < grants.Count; i++)
            {
                var g = grants[i];
                if (g.Target != ModifierTarget.Player) continue;
                if (gatedOnly && g.GateWeapon == null) continue;
                Refresh(g, ps, LiveOnPlayer(g));
            }
        }

        private static void Refresh(ModifierGrant g, StatContainer container, bool live)
        {
            if (container == null) return;
            container.RemoveAllFromOwner(g);
            if (live) container.AddModifier(g.CreateModifier(), g);
        }

        private bool LiveOnWeapon(ModifierGrant g, WeaponDamageSource w)
        {
            if (!bound.Contains(w) || !g.Matches(w)) return false;
            return g.Scope == ActivityScope.InLoadout || w == held;
        }

        private bool LiveOnPlayer(ModifierGrant g)
        {
            if (g.GateWeapon == null) return true;
            if (!bound.Contains(g.GateWeapon)) return false;
            return g.Scope == ActivityScope.InLoadout || g.GateWeapon == held;
        }
    }
}