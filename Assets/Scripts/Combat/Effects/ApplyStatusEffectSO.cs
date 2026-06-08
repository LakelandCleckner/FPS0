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

        protected override IHitEffect Build()
        {
            return new ApplyStatusHitEffect(status);
        }
    }
}
