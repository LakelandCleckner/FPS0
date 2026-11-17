using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Info;

namespace Combat.Perks
{
    // Adds on-hit effects to its weapon (burn, explode on kill). Replaces weapon riders.
    [CreateAssetMenu(fileName = "EffectPerk", menuName = "Combat/Perks/Effect Perk")]
    public class EffectPerkSO : PerkSO
    {
        public List<HitEffectSO> effects = new List<HitEffectSO>();
        public override string TriggerGroup => "On hit";

        public override void CollectElements(List<Combat.Core.DamageTypeSO> into)
        {
            foreach (var e in effects) if (e != null && e.Element != null) into.Add(e.Element);
        }

        protected override void DescribeAuto(Describer d)
        {
            foreach (var e in effects)
            {
                if (e == null) continue;
                d.Next();
                e.Describe(d);
            }
            if (d.IsEmpty) d.Add(description);
        }


        public override PerkRuntime CreateRuntime() => new EffectPerkRuntime();

        private sealed class EffectPerkRuntime : PerkRuntime
        {
            private readonly List<IHitEffect> added = new List<IHitEffect>();

            protected override void OnAttach()
            {
                if (Ctx.IsArmor)
                {
                    Debug.LogWarning($"[EffectPerk] '{Definition.name}' needs a weapon; it does nothing on armor.");
                    return;
                }

                foreach (var so in ((EffectPerkSO)Definition).effects)
                {
                    if (so == null) continue;
                    var fx = so.GetInstance();
                    if (fx == null) continue;
                    Ctx.Weapon.AddEffect(fx);
                    added.Add(fx);
                }
            }

            protected override void OnDetach()
            {
                for (int i = 0; i < added.Count; i++) Ctx.Weapon.RemoveEffect(added[i]);
                added.Clear();
            }
        }
    }
}
