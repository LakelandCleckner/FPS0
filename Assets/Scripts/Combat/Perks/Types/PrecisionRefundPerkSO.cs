using UnityEngine;
using Combat.Events;

namespace Combat.Perks
{
    // Every N precision shots puts rounds back in the magazine. Counts shots, so pellets and pierce count once.
    [CreateAssetMenu(fileName = "PrecisionRefundPerk", menuName = "Combat/Perks/Precision Refund Perk")]
    public class PrecisionRefundPerkSO : PerkSO
    {
        [Min(1)] public int everyN = 3;
        [Min(1)] public int rounds = 1;
        [Tooltip("Take the rounds from reserves instead of granting them free.")]
        public bool fromReserves = false;

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private int count;
            private int lastShot = -1;

            private PrecisionRefundPerkSO Def => (PrecisionRefundPerkSO)Definition;

            public override bool TryGetStatus(out PerkStatus s)
            {
                s = default;
                s.Count = count;
                s.CountMax = Def.everyN;
                s.Active = count > 0;
                return s.Active;
            }

            protected override void OnAttach() => Listen(WeaponEventType.PrecisionHit, HitSourceMask.Direct);

            protected override void OnEvent(in WeaponEvent evt)
            {
                if (Ctx.Ammo == null) return;

                int shot = evt.Hit.Shot.ShotId;
                if (shot != 0 && shot == lastShot) return;
                lastShot = shot;

                if (++count < Def.everyN) { NotifyStatus(); return; }
                count = 0;
                Ctx.Ammo.AddToMagazine(Def.rounds, Def.fromReserves);
                NotifyStatus();
            }
        }
    }
}