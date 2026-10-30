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

    // Per-item perk instance: holds state, hooks events, timers and modifiers. Detach cleans everything up.
    public abstract class PerkRuntime
    {
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
            OnAttach();
        }

        internal void Detach()
        {
            try { OnDetach(); }
            catch (Exception e) { Debug.LogError($"[Perk] {Definition.name} OnDetach threw: {e}"); }

            if (Ctx.Bus != null) Ctx.Bus.UnsubscribeAll(this);
            if (Ctx.Applier != null) Ctx.Applier.Unregister(this);
            if (Ctx.Clock != null) Ctx.Clock.CancelAll(this);
        }

        // The host updates Ctx.IsHeld first, then notifies each runtime.
        internal void NotifyHeld(bool held) => OnHeldChanged(held);

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
                if (duration > 0f) timer = owner.After(duration, Clear);
            }

            public void Clear()
            {
                owner.CancelTimer(ref timer);
                for (int i = 0; i < grants.Count; i++) owner.Remove(grants[i]);
                grants.Clear();
                Stacks = 0;
            }
        }
    }
}