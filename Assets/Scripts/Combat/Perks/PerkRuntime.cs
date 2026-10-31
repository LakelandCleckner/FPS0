using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Loadout;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Perks
{
    // What a perk can reach. Built by WeaponPerks or ArmorPerks.
    public sealed class PerkContext
    {
        public object Item;                    // WeaponInstance or ArmorInstance
        public WeaponDamageSource Weapon;      // null for armour
        public WeaponAmmo Ammo;                // null for armour
        public WeaponEventBus Bus;
        public LoadoutModifierApplier Applier;
        public PerkClock Clock;
        public WeaponLoadout Loadout;
        public CombatantStats Player;
        public CombatantHealth Health;
        public bool IsHeld;

        public bool IsArmor => Weapon == null;
    }

    // What the HUD shows for a perk right now.
    public struct PerkStatus
    {
        public bool Active;        // show the tile
        public int Stacks;         // shown when above 1
        public float ExpiresAt;    // Time.time the effect ends; 0 = no timer
        public float Duration;     // full length of that timer
        public int Count;          // progress toward a trigger, e.g. 2 of 3
        public int CountMax;       // 0 = no counter
    }

    // Per-item perk instance: holds state, hooks events, timers and modifiers. Detach cleans everything up.
    public abstract class PerkRuntime
    {
        // Raised when any perk's HUD status changes, including on detach.
        public static event Action<PerkRuntime> AnyStatusChanged;

        public PerkSO Perk => Definition;
        public bool Detached { get; private set; }

        // Armour perks, or a weapon perk whose weapon is in hand. The HUD dims the rest.
        public bool InHand => Ctx != null && (Ctx.IsArmor || Ctx.IsHeld);

        // False = nothing to show. Perks with HUD state override this.
        public virtual bool TryGetStatus(out PerkStatus status)
        {
            status = default;
            return false;
        }

        protected void NotifyStatus() => AnyStatusChanged?.Invoke(this);

        protected PerkSO Definition { get; private set; }
        protected PerkContext Ctx { get; private set; }

        protected bool IsHeld => Ctx.IsHeld;

        // Whether this perk hears bus events right now.
        protected bool Hears
        {
            get
            {
                if (Ctx.IsArmor) return true;
                switch (Definition.listenScope)
                {
                    case PerkListenScope.Always: return true;
                    case PerkListenScope.StowedOnly: return !Ctx.IsHeld;
                    default: return Ctx.IsHeld;
                }
            }
        }

        internal void Attach(PerkSO definition, PerkContext ctx)
        {
            Definition = definition;
            Ctx = ctx;
            Detached = false;
            OnAttach();
            NotifyStatus();
        }

        internal void Detach()
        {
            try { OnDetach(); }
            catch (Exception e) { Debug.LogError($"[Perk] {Definition.name} OnDetach threw: {e}"); }

            if (Ctx.Bus != null) Ctx.Bus.UnsubscribeAll(this);
            if (Ctx.Applier != null) Ctx.Applier.Unregister(this);
            if (Ctx.Clock != null) Ctx.Clock.CancelAll(this);

            Detached = true;
            NotifyStatus();
        }

        // The host updates Ctx.IsHeld first, then notifies each runtime.
        internal void NotifyHeld(bool held)
        {
            OnHeldChanged(held);
            NotifyStatus();
        }

        public override string ToString() => Definition != null ? "perk " + Definition.name : base.ToString();

        protected virtual void OnAttach() { }
        protected virtual void OnDetach() { }
        protected virtual void OnHeldChanged(bool held) { }
        protected virtual void OnEvent(in WeaponEvent evt) { }

        // ------------------------------------------------------------------ events

        // Own weapon's events by default; anyWeapon hears every weapon (armour always does).
        protected void Listen(WeaponEventType type, HitSourceMask sources = HitSourceMask.Direct, bool anyWeapon = false)
        {
            if (Ctx.Bus == null) return;
            var filter = anyWeapon || Ctx.IsArmor ? null : Ctx.Weapon;
            Ctx.Bus.Subscribe(type, Dispatch, filter, sources);
        }

        private void Dispatch(WeaponEvent evt)
        {
            if (Hears) OnEvent(in evt);
        }

        // ------------------------------------------------------------------ modifiers

        // Weapons target: this weapon (weapon perk) or filtered loadout weapons (armour).
        // Player target: gated by this weapon's state (weapon perk) or always (armour).
        protected ModifierGrant Grant(ModifierEntry e, float value)
        {
            if (e == null || e.stat == null || Ctx.Applier == null) return null;

            WeaponDamageSource only = null, gate = null;
            IReadOnlyList<WeaponTypeSO> types = null;

            if (e.target == ModifierTarget.Weapons)
            {
                if (Ctx.IsArmor) types = e.weaponTypes;
                else only = Ctx.Weapon;
            }
            else if (!Ctx.IsArmor)
            {
                gate = Ctx.Weapon;
            }

            var g = new ModifierGrant(this, e.stat, e.bucket, value, e.target, e.scope, types, only, gate);
            Ctx.Applier.Register(g);
            return g;
        }

        protected void SetValue(ModifierGrant g, float value)
        {
            if (g != null && Ctx.Applier != null) Ctx.Applier.SetValue(g, value);
        }

        protected void Remove(ModifierGrant g)
        {
            if (g != null && Ctx.Applier != null) Ctx.Applier.Remove(g);
        }

        // ------------------------------------------------------------------ timers

        protected long After(float delay, Action callback)
            => Ctx.Clock != null ? Ctx.Clock.Schedule(this, delay, callback) : 0;

        protected void CancelTimer(ref long id)
        {
            if (id != 0 && Ctx.Clock != null) Ctx.Clock.Cancel(id);
            id = 0;
        }

        // ------------------------------------------------------------------ buff helper

        // Stacking timed buff: each stack adds the entries' values; any new stack restarts the timer.
        protected sealed class TimedBuff
        {
            private readonly PerkRuntime owner;
            private readonly List<ModifierEntry> entries;
            private readonly List<ModifierGrant> grants = new List<ModifierGrant>();
            private readonly float duration;
            private readonly int maxStacks;
            private long timer;

            public int Stacks { get; private set; }
            public float ExpiresAt { get; private set; }
            public float Duration => duration;

            // Fills the timer and stack fields of a HUD status.
            public void Describe(ref PerkStatus s)
            {
                s.Active |= Stacks > 0;
                s.Stacks = Stacks;
                s.ExpiresAt = Stacks > 0 ? ExpiresAt : 0f;
                s.Duration = duration;
            }

            public TimedBuff(PerkRuntime owner, List<ModifierEntry> entries, float duration, int maxStacks)
            {
                this.owner = owner;
                this.entries = entries;
                this.duration = duration;
                this.maxStacks = Mathf.Max(1, maxStacks);
            }

            public void AddStack()
            {
                Stacks = Mathf.Min(Stacks + 1, maxStacks);

                if (grants.Count == 0)
                {
                    foreach (var e in entries)
                    {
                        var g = owner.Grant(e, e != null ? e.value * Stacks : 0f);
                        if (g != null) grants.Add(g);
                    }
                }
                else
                {
                    int gi = 0;
                    foreach (var e in entries)
                    {
                        if (e == null || e.stat == null) continue;
                        owner.SetValue(grants[gi++], e.value * Stacks);
                    }
                }

                owner.CancelTimer(ref timer);
                if (duration > 0f)
                {
                    timer = owner.After(duration, Clear);
                    ExpiresAt = Time.time + duration;
                }
                owner.NotifyStatus();
            }

            public void Clear()
            {
                owner.CancelTimer(ref timer);
                for (int i = 0; i < grants.Count; i++) owner.Remove(grants[i]);
                grants.Clear();

                bool changed = Stacks > 0;
                Stacks = 0;
                ExpiresAt = 0f;
                if (changed) owner.NotifyStatus();
            }
        }
    }
}