using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Loadout;
using Combat.Info;

namespace Combat.Perks
{
    // On an event, gain a stacking timed buff. Covers kill ramps, kill mobility and similar.
    [CreateAssetMenu(fileName = "TriggeredBuffPerk", menuName = "Combat/Perks/Triggered Buff Perk")]
    public class TriggeredBuffPerkSO : PerkSO
    {
        [Header("Trigger")]
        public WeaponEventType trigger = WeaponEventType.Kill;
        [Tooltip("Hit-family triggers only. Add StatusTick to count kills by burns.")]
        public HitSourceMask sources = HitSourceMask.Direct | HitSourceMask.StatusTick;
        [Tooltip("Weapon perk: hear every loadout weapon, not just this one.")]
        public bool anyWeapon = false;
        [Tooltip("Hit-family triggers: count each shot once, so pellets and pierce don't multiply it.")]
        public bool oncePerShot = false;
        [Tooltip("Triggers needed per stack.")]
        [Min(1)] public int everyN = 1;
        [Tooltip("The N triggers must land within this many seconds. 0 = no window.")]
        [Min(0f)] public float window = 0f;

        [Header("Buff")]
        [Tooltip("Seconds. Each new stack restarts it. 0 = until removed.")]
        [Min(0f)] public float duration = 5f;
        [Min(1)] public int maxStacks = 1;
        [Tooltip("Weapon perk: the buff ends after this many shots of this weapon. 0 = timer only.")]
        [Min(0)] public int charges = 0;
        [Tooltip("Weapon perk: the buff ends when this weapon starts being stowed.")]
        public bool endOnStow = false;
        [Tooltip("Per stack; values add up across stacks.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        private void Reset() => listenScope = PerkListenScope.Always;
        private bool UsesCharges => charges > 0 && trigger != WeaponEventType.ShotFired;
        public override string TriggerGroup => Combat.Info.Describer.Trigger(trigger);

        // Short buffs on shots read as sustained fire.
        private bool Sustained => trigger == WeaponEventType.ShotFired && duration > 0f && duration <= 1f;

        protected override void DescribeAuto(Describer d)
        {
            if (Sustained)
            {
                d.Add("While firing, every ").Count(everyN, "count").Add(everyN == 1 ? " shot" : " shots").Add(" grants ").Modifiers(modifiers).Add(".");
                d.Next().Add(maxStacks > 1 ? "Stacks up to " : "");
                if (maxStacks > 1) d.Count(maxStacks, "stacks").Add(" times. Stacks fall off ");
                else d.Add("Ends ");
                d.Seconds(duration, "duration").Add(" after you stop firing.");
                if (endOnStow) d.Next().Add("Ends when the weapon is stowed.");
                return;
            }

            d.Every(everyN, trigger, window, sources, anyWeapon);
            d.Add("gain ").Modifiers(modifiers);
            if (UsesCharges) d.Add(" for the next ").Count(charges, "charges").Add(charges == 1 ? " shot" : " shots");
            if (duration > 0f) d.Add(UsesCharges ? " or " : " for ").Seconds(duration, "duration");
            d.Add(".");
            if (maxStacks > 1) d.Next().Add("Stacks up to ").Count(maxStacks, "stacks").Add(" times; each stack refreshes the timer.");
            if (endOnStow) d.Next().Add("Ends when the weapon is stowed.");
        }


        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private TimedBuff buff;
            private readonly Queue<float> times = new Queue<float>();
            private int count;
            private int lastShot = -1;
            private object lastShotSource;
            private int shotsLeft;

            private TriggeredBuffPerkSO Def => (TriggeredBuffPerkSO)Definition;

            public override bool TryGetStatus(out PerkStatus s)
            {
                s = default;
                buff.Describe(ref s);
                if (Def.everyN > 1)
                {
                    s.Count = Def.window > 0f ? times.Count : count;
                    s.CountMax = Def.everyN;
                    s.Active |= s.Count > 0;
                }
                return s.Active;
            }

            protected override void OnAttach()
            {
                buff = new TimedBuff(this, Def.modifiers, Def.duration, Def.maxStacks);
                Listen(Def.trigger, Def.sources, Def.anyWeapon);
                if (Def.UsesCharges) Listen(WeaponEventType.ShotFired);
            }

            protected override void OnDetach() => buff.Clear();

            protected override void OnHeldChanged(bool held)
            {
                if (!held && Def.endOnStow) buff.Clear();
            }

            protected override void OnEvent(in WeaponEvent evt)
            {
                // Charges: each shot of this weapon spends one while the buff is up.
                if (Def.UsesCharges && evt.Type == WeaponEventType.ShotFired)
                {
                    if (buff.Stacks > 0 && --shotsLeft <= 0) buff.Clear();
                    return;
                }

                if (Def.oncePerShot && evt.IsHitEvent)
                {
                    var hit = evt.Hit;
                    int shot = hit.Shot.ShotId;
                    if (shot != 0 && shot == lastShot && ReferenceEquals(hit.DamageSource, lastShotSource)) return;
                    lastShot = shot;
                    lastShotSource = hit.DamageSource;
                }

                if (!Counted()) { NotifyStatus(); return; }
                buff.AddStack();
                shotsLeft = Def.charges;
            }

            // True when this trigger completes a set of N.
            private bool Counted()
            {
                if (Def.everyN <= 1) return true;

                if (Def.window <= 0f)
                {
                    if (++count < Def.everyN) return false;
                    count = 0;
                    return true;
                }

                float now = Time.time;
                times.Enqueue(now);
                while (times.Count > 0 && now - times.Peek() > Def.window) times.Dequeue();
                if (times.Count < Def.everyN) return false;
                times.Clear();
                return true;
            }
        }
    }
}