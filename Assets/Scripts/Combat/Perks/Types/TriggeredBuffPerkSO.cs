using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Loadout;

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
        [Tooltip("Weapon perk: the buff ends when this weapon starts being stowed.")]
        public bool endOnStow = false;
        [Tooltip("Per stack; values add up across stacks.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        private void Reset() => listenScope = PerkListenScope.Always;

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private TimedBuff buff;
            private readonly Queue<float> times = new Queue<float>();
            private int count;
            private int lastShot = -1;
            private object lastShotSource;

            private TriggeredBuffPerkSO Def => (TriggeredBuffPerkSO)Definition;

            protected override void OnAttach()
            {
                buff = new TimedBuff(this, Def.modifiers, Def.duration, Def.maxStacks);
                Listen(Def.trigger, Def.sources, Def.anyWeapon);
            }

            protected override void OnDetach() => buff.Clear();

            protected override void OnHeldChanged(bool held)
            {
                if (!held && Def.endOnStow) buff.Clear();
            }

            protected override void OnEvent(in WeaponEvent evt)
            {
                if (Def.oncePerShot && evt.IsHitEvent)
                {
                    var hit = evt.Hit;
                    int shot = hit.Shot.ShotId;
                    if (shot != 0 && shot == lastShot && ReferenceEquals(hit.DamageSource, lastShotSource)) return;
                    lastShot = shot;
                    lastShotSource = hit.DamageSource;
                }

                if (!Counted()) return;
                buff.AddStack();
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
