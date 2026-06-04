using UnityEngine;
using Combat.Core;

namespace Combat.Effects
{
    // Authorable debug effect. Pick which phase it runs in so you can drop it
    // into the list in any order and prove phase-sorting works.
    [CreateAssetMenu(fileName = "DebugLogEffect", menuName = "Combat/Effects/Debug Log")]
    public class DebugLogEffectSO : HitEffectSO
    {
        public EffectPhase phase = EffectPhase.Modifier;
        public string label = "debug";

        protected override IHitEffect Build()
        {
            return new DebugLogHitEffect(phase, label);
        }
    }
}
