using UnityEngine;
using Combat.Info;

namespace Combat.Perks
{
    // Stowed for a while, the weapon reloads itself.
    [CreateAssetMenu(fileName = "HolsterReloadPerk", menuName = "Combat/Perks/Holster Reload Perk")]
    public class HolsterReloadPerkSO : PerkSO
    {
        [Tooltip("Seconds stowed before the reload.")]
        [Min(0f)] public float delay = 3f;

        private void Reset() => listenScope = PerkListenScope.StowedOnly;
        public override string TriggerGroup => "While stowed";

        protected override void DescribeAuto(Describer d)
        {
            d.Add("Reloads itself after being stowed for ").Seconds(delay, "delay").Add(".");
        }


        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private long timer;

            protected override void OnAttach()
            {
                if (!Ctx.IsArmor && !IsHeld) Arm();
            }

            protected override void OnHeldChanged(bool held)
            {
                if (held) CancelTimer(ref timer);
                else Arm();
            }

            private void Arm()
            {
                CancelTimer(ref timer);
                timer = After(((HolsterReloadPerkSO)Definition).delay, Reload);
            }

            private void Reload()
            {
                timer = 0;
                if (Ctx.Ammo != null) Ctx.Ammo.RefillInstant();
            }
        }
    }
}
