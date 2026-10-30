using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Loadout;
using Combat.Sources;
using Combat.Weapons;

namespace Combat.Perks
{
    // Land a hit with every other loadout weapon within a window, and this weapon gets a timed buff.
    [CreateAssetMenu(fileName = "SwapSynergyPerk", menuName = "Combat/Perks/Swap Synergy Perk")]
    public class SwapSynergyPerkSO : PerkSO
    {
        [Tooltip("Seconds within which every other loadout weapon must have landed a hit.")]
        [Min(0.1f)] public float window = 6f;
        [Min(0f)] public float duration = 8f;
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        private void Reset() => listenScope = PerkListenScope.Always;

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private TimedBuff buff;
            private readonly Dictionary<WeaponDamageSource, float> lastHit = new Dictionary<WeaponDamageSource, float>();

            private SwapSynergyPerkSO Def => (SwapSynergyPerkSO)Definition;

            protected override void OnAttach()
            {
                buff = new TimedBuff(this, Def.modifiers, Def.duration, 1);
                if (!Ctx.IsArmor) Listen(WeaponEventType.Hit, HitSourceMask.Direct, anyWeapon: true);
            }

            protected override void OnDetach() => buff.Clear();

            protected override void OnEvent(in WeaponEvent evt)
            {
                var w = evt.Weapon;
                if (w == null || w == Ctx.Weapon || Ctx.Loadout == null) return;

                lastHit[w] = Time.time;
                if (!AllOthersHit()) return;

                lastHit.Clear();
                buff.AddStack();
            }

            private bool AllOthersHit()
            {
                float now = Time.time;
                int others = 0;

                for (int i = 0; i < LoadoutSlots.Count; i++)
                {
                    var c = Ctx.Loadout.ControllerIn(i);
                    var src = c != null ? c.DamageSource : null;
                    if (src == null || src == Ctx.Weapon) continue;

                    others++;
                    if (!lastHit.TryGetValue(src, out float t) || now - t > Def.window) return false;
                }
                return others > 0;
            }
        }
    }
}
