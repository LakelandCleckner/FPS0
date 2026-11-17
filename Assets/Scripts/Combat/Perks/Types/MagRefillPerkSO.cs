using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Info;

namespace Combat.Perks
{
    // On an event (every N, optionally within a window), puts rounds or a share of the magazine back in.
    [CreateAssetMenu(fileName = "MagRefillPerk", menuName = "Combat/Perks/Mag Refill Perk")]
    public class MagRefillPerkSO : PerkSO
    {
        [Header("Trigger")]
        public WeaponEventType trigger = WeaponEventType.Kill;
        [Tooltip("Hit-family triggers only. Add StatusTick to count kills by burns.")]
        public HitSourceMask sources = HitSourceMask.Direct | HitSourceMask.StatusTick;
        [Tooltip("Hit-family triggers: count each shot once, so pellets and pierce don't multiply it.")]
        public bool oncePerShot = true;
        [Tooltip("Triggers needed per refill.")]
        [Min(1)] public int everyN = 1;
        [Tooltip("The N triggers must land within this many seconds. 0 = no window.")]
        [Min(0f)] public float window = 0f;

        [Header("Refill")]
        [Tooltip("Flat rounds per refill.")]
        [Min(0)] public int rounds = 0;
        [Tooltip("Share of the magazine size per refill (0.2 = 20%), rounded up.")]
        [Range(0f, 1f)] public float magFraction = 0.2f;
        [Tooltip("Take the rounds from reserves instead of granting them free.")]
        public bool fromReserves = true;

        private void Reset() => listenScope = PerkListenScope.Always;
        public override string TriggerGroup => Describer.Trigger(trigger);

        protected override void DescribeAuto(Describer d)
        {
            d.Every(everyN, trigger, window, sources, false);
            d.Add("put ");
            if (magFraction > 0f) d.Percent(magFraction, "fraction").Add(" of the magazine");
            if (magFraction > 0f && rounds > 0) d.Add(" plus ");
            if (rounds > 0) d.Count(rounds, "rounds").Add(rounds == 1 ? " round" : " rounds");
            d.Add(" back in").Add(fromReserves ? " from reserves." : ".");
        }

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private readonly Queue<float> times = new Queue<float>();
            private int count;
            private int lastShot = -1;
            private object lastShotSource;

            private MagRefillPerkSO Def => (MagRefillPerkSO)Definition;

            public override bool TryGetStatus(out PerkStatus s)
            {
                s = default;
                if (Def.everyN <= 1) return false;
                s.Count = Def.window > 0f ? times.Count : count;
                s.CountMax = Def.everyN;
                s.Active = s.Count > 0;
                return s.Active;
            }

            protected override void OnAttach() => Listen(Def.trigger, Def.sources);

            protected override void OnEvent(in WeaponEvent evt)
            {
                if (Ctx.Ammo == null) return;
                if (Def.oncePerShot && evt.IsHitEvent)
                {
                    var hit = evt.Hit;
                    int shot = hit.Shot.ShotId;
                    if (shot != 0 && shot == lastShot && ReferenceEquals(hit.DamageSource, lastShotSource)) return;
                    lastShot = shot;
                    lastShotSource = hit.DamageSource;
                }

                if (!Counted()) { NotifyStatus(); return; }
                int n = Def.rounds + Mathf.CeilToInt(Def.magFraction * Ctx.Ammo.MagSize);
                if (n > 0) Ctx.Ammo.AddToMagazine(n, Def.fromReserves);
                NotifyStatus();
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
