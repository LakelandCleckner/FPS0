using System.Collections.Generic;
using UnityEngine;
using Combat.Events;
using Combat.Loadout;

namespace Combat.Perks
{
    // Modifiers that scale with how full the magazine is. Updated on ammo changes, never polled.
    [CreateAssetMenu(fileName = "MagScaledPerk", menuName = "Combat/Perks/Mag Scaled Perk")]
    public class MagScaledPerkSO : PerkSO
    {
        [Tooltip("Scale with how EMPTY the magazine is instead of how full.")]
        public bool invert = false;
        [Tooltip("Maps fill (0..1) to strength (0..1). Empty = linear.")]
        public AnimationCurve curve = new AnimationCurve();
        [Tooltip("Values at full strength.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        private void Reset() => listenScope = PerkListenScope.Always;

        public override PerkRuntime CreateRuntime() => new Runtime();

        private sealed class Runtime : PerkRuntime
        {
            private readonly List<(ModifierGrant grant, float full)> grants = new List<(ModifierGrant, float)>();

            private MagScaledPerkSO Def => (MagScaledPerkSO)Definition;

            protected override void OnAttach()
            {
                if (Ctx.IsArmor || Ctx.Ammo == null) return;

                float s = Strength();
                foreach (var e in Def.modifiers)
                {
                    var g = Grant(e, e != null ? e.value * s : 0f);
                    if (g != null) grants.Add((g, e.value));
                }

                Listen(WeaponEventType.AmmoChanged);
            }

            protected override void OnEvent(in WeaponEvent evt)
            {
                float s = Strength();
                for (int i = 0; i < grants.Count; i++) SetValue(grants[i].grant, grants[i].full * s);
            }

            private float Strength()
            {
                var ammo = Ctx.Ammo;
                float fill = ammo.MagSize > 0 ? Mathf.Clamp01((float)ammo.Magazine / ammo.MagSize) : 0f;
                if (Def.invert) fill = 1f - fill;

                var c = Def.curve;
                return c != null && c.length > 0 ? c.Evaluate(fill) : fill;
            }
        }
    }
}
