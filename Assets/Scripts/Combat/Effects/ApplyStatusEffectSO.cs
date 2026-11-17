using UnityEngine;
using Combat.Core;
using Combat.Status;

namespace Combat.Effects
{
    // Authorable "applies a status on hit" effect. Drop into a weapon's effect
    // list to make it apply burn/slow/etc.
    [CreateAssetMenu(fileName = "ApplyStatusEffect", menuName = "Combat/Effects/Apply Status")]
    public class ApplyStatusEffectSO : HitEffectSO
    {
        public StatusSO status;

        public override DamageTypeSO Element => status != null ? status.damageType : null;

        public override void Describe(Combat.Info.Describer d)
        {
            if (status == null) return;
            d.Add("Hits apply ").StatusLink(status).Add(": ");
            status.Describe(d);
        }

        protected override IHitEffect Build()
        {
            return new ApplyStatusHitEffect(status);
        }
    }
}
