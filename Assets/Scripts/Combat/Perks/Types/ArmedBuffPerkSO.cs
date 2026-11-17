using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Loadout;
using Combat.Info;

namespace Combat.Perks
{
    // One event arms the perk; a second event within the window spends it for a timed buff.
    // e.g. a kill, then a reload within 4 seconds: more damage for 5 seconds.
    [CreateAssetMenu(fileName = "ArmedBuffPerk", menuName = "Combat/Perks/Armed Buff Perk")]
    public class ArmedBuffPerkSO : PerkSO
    {
        [Header("Arm")]
        public WeaponEventType armTrigger = WeaponEventType.Kill;
        [Tooltip("Hit-family arm triggers only. Add StatusTick to count kills by burns.")]
        public HitSourceMask armSources = HitSourceMask.Direct | HitSourceMask.StatusTick;
        [Tooltip("Seconds the perk stays armed.")]
        [Min(0.1f)] public float armWindow = 4f;

        [Header("Spend")]
        public WeaponEventType spendTrigger = WeaponEventType.ReloadComplete;
        [Tooltip("Hit-family spend triggers only.")]
        public HitSourceMask spendSources = HitSourceMask.Direct;

        [Header("Buff")]
        [Tooltip("Seconds. 0 = until removed.")]
        [Min(0f)] public float duration = 5f;
        [Min(1)] public int maxStacks = 1;
        [Tooltip("The buff ends after this many shots. 0 = timer only.")]
        [Min(0)] public int charges = 0;
        [Tooltip("The buff ends when this weapon starts being stowed.")]
        public bool endOnStow = false;
        [Tooltip("Per stack; values add up across stacks.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        private void Reset() => listenScope = PerkListenScope.Always;
        public override string TriggerGroup => Describer.Trigger(armTrigger);

        protected override void DescribeAuto(Describer d)
        {
            d.When(armTrigger, armSources, false);
            d.Add(Lower(Describer.Trigger(spendTrigger))).Add(" within ").Seconds(armWindow, "window").Add(" grants ")
             .Modifiers(modifiers);
            if (charges > 0) d.Add(" for the next ").Count(charges, "charges").Add(charges == 1 ? " shot" : " shots");
            if (duration > 0f) d.Add(charges > 0 ? " or " : " for ").Seconds(duration, "duration");
            d.Add(".");
            if (maxStacks > 1) d.Next().Add("Stacks up to ").Count(maxStacks, "stacks").Add(" times.");
            if (endOnStow) d.Next().Add("Ends when the weapon is stowed.");
        }

        private static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private TimedBuff buff;
            private float armedUntil;
            private int shotsLeft;

            private ArmedBuffPerkSO Def => (ArmedBuffPerkSO)Definition;
            private bool Armed => Time.time <= armedUntil;

            public override bool TryGetStatus(out PerkStatus s)
            {
                s = default;
                buff.Describe(ref s);
                if (buff.Stacks == 0 && Armed)
                {
                    s.Active = true;
                    s.ExpiresAt = armedUntil;
                    s.Duration = Def.armWindow;
                }
                return s.Active;
            }

            protected override void OnAttach()
            {
                buff = new TimedBuff(this, Def.modifiers, Def.duration, Def.maxStacks);
                Listen(Def.armTrigger, Def.armSources);
                if (Def.spendTrigger != Def.armTrigger) Listen(Def.spendTrigger, Def.spendSources);
                if (Def.charges > 0 && Def.spendTrigger != WeaponEventType.ShotFired && Def.armTrigger != WeaponEventType.ShotFired)
                    Listen(WeaponEventType.ShotFired);
            }

            protected override void OnDetach() => buff.Clear();

            protected override void OnHeldChanged(bool held)
            {
                if (!held && Def.endOnStow) buff.Clear();
            }

            protected override void OnEvent(in WeaponEvent evt)
            {
                if (evt.Type == Def.spendTrigger && Armed)
                {
                    armedUntil = 0f;
                    buff.AddStack();
                    shotsLeft = Def.charges;
                    return;
                }

                if (evt.Type == Def.armTrigger)
                {
                    armedUntil = Time.time + Def.armWindow;
                    NotifyStatus();
                    return;
                }

                // Charges: each shot spends one while the buff is up.
                if (evt.Type == WeaponEventType.ShotFired && Def.charges > 0 && buff.Stacks > 0 && --shotsLeft <= 0)
                    buff.Clear();
            }
        }
    }
}
