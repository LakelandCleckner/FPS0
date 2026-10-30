using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Loadout
{
    // The one place loadout-driven modifiers are registered: armour now, perks later.
    // Each grant is its own modifier owner, so a refresh removes by owner and re-adds if still live.
    public class LoadoutModifierApplier : MonoBehaviour
    {
        [Tooltip("Found on this object or its parents if empty.")]
        [SerializeField] private PlayerLoadout model;
        [Tooltip("Found on this object or its children if empty.")]
        [SerializeField] private WeaponLoadout weapons;
        [Tooltip("The player's stats. Found on this object or its parents if empty.")]
        [SerializeField] private CombatantStats player;

        [SerializeField] private bool debugLog = false;

        private readonly List<ModifierGrant> grants = new List<ModifierGrant>();
        private readonly HashSet<WeaponDamageSource> bound = new HashSet<WeaponDamageSource>();
        private WeaponDamageSource held;
        private bool armorRegistered;

        private StatContainer PlayerStats => player != null ? player.Container : null;

        private void Awake()
        {
            if (model == null) model = GetComponentInParent<PlayerLoadout>();
            if (weapons == null) weapons = GetComponentInChildren<WeaponLoadout>();
            if (player == null) player = GetComponentInParent<CombatantStats>();

            if (model == null) Debug.LogError("[ModifierApplier] No PlayerLoadout found.");
            if (player == null) Debug.LogWarning("[ModifierApplier] No CombatantStats found; player modifiers are skipped.");
        }

        // Subscribe only; containers may not exist until every Awake has run.
        private void OnEnable()
        {
            if (model != null) model.ArmorSlotChanged += HandleArmorChanged;
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
            if (model != null) model.ArmorSlotChanged -= HandleArmorChanged;
            if (weapons != null)
            {
                weapons.WeaponBound -= HandleBound;
                weapons.WeaponUnbound -= HandleUnbound;
                weapons.OnWeaponEquipped -= HandleEquipped;
                weapons.OnStowStarted -= HandleStowStarted;
            }
        }

        private void Start() => EnsureArmor();

        // ------------------------------------------------------------------ public API

        public void Register(ModifierGrant grant)
        {
            if (grant == null || grant.Stat == null) return;
            grants.Add(grant);
            Apply(grant);
        }

        // Removes every grant this owner registered, from every container.
        public void Unregister(object owner)
        {
            if (owner == null) return;

            for (int i = grants.Count - 1; i >= 0; i--)
            {
                var g = grants[i];
                if (!ReferenceEquals(g.Owner, owner)) continue;

                var ps = PlayerStats;
                if (ps != null) ps.RemoveAllFromOwner(g);
                foreach (var w in bound)
                {
                    var c = w.SourceStats;
                    if (c != null) c.RemoveAllFromOwner(g);
                }
                grants.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ armour

        // Lazy so it works whichever of this and WeaponLoadout starts first.
        private void EnsureArmor()
        {
            if (armorRegistered || model == null) return;
            armorRegistered = true;

            model.EnsureInitialized();
            for (int i = 0; i < model.ArmorSlotCount; i++)
                RegisterArmor(model.GetArmor(i));
        }

        private void RegisterArmor(ArmorInstance armor)
        {
            var def = armor != null ? armor.Definition : null;
            if (def == null) return;

            foreach (var e in def.modifiers)
            {
                if (e == null || e.stat == null) continue;
                Register(new ModifierGrant(armor, e.stat, e.bucket, e.value, e.target, e.scope, e.weaponTypes));
            }

            if (debugLog) Debug.Log($"[ModifierApplier] Registered armour '{def.name}'");
        }

        private void HandleArmorChanged(ArmorSlotSO slot, ArmorInstance oldArmor, ArmorInstance newArmor)
        {
            // Not registered yet: registering now reads the model, which already holds the change.
            if (!armorRegistered)
            {
                EnsureArmor();
                return;
            }

            Unregister(oldArmor);
            RegisterArmor(newArmor);
        }

        // ------------------------------------------------------------------ weapons

        private void HandleBound(int slot, WeaponDamageSource weapon)
        {
            EnsureArmor();
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
            EnsureArmor();
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
