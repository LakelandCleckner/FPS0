using UnityEngine;
using Combat.Core;
using Combat.Events;

namespace Combat.Perks
{
    // Heals a share of max health on kill.
    [CreateAssetMenu(fileName = "HealOnKillPerk", menuName = "Combat/Perks/Heal On Kill Perk")]
    public class HealOnKillPerkSO : PerkSO
    {
        [Tooltip("Fraction of max health (0.1 = 10%).")]
        [Range(0f, 1f)] public float healFraction = 0.1f;
        [Tooltip("Count kills by burns and chains, not just shots.")]
        public HitSourceMask sources = HitSourceMask.All;
        [Tooltip("Weapon perk: heal on kills by any loadout weapon. Armour always does.")]
        public bool anyWeapon = false;

        private void Reset() => listenScope = PerkListenScope.Always;

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private HealOnKillPerkSO Def => (HealOnKillPerkSO)Definition;

            protected override void OnAttach() => Listen(WeaponEventType.Kill, Def.sources, Def.anyWeapon);

            protected override void OnEvent(in WeaponEvent evt)
            {
                var h = Ctx.Health;
                if (h == null || h.IsDying) return;
                h.Heal(h.MaxHealth * Def.healFraction);
            }
        }
    }
}
