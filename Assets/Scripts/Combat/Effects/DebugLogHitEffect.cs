using UnityEngine;
using Combat.Core;

namespace Combat.Effects
{
    // Trivial, harmless effect purely for proving the resolver loops the effect
    // list AND sorts by phase. Logs its phase and the context's damage-so-far.
    //
    // Stateless (reads context, writes nothing gameplay-affecting) so it follows
    // the cached-singleton contract.
    public class DebugLogHitEffect : IHitEffect
    {
        public EffectPhase Phase { get; }
        public bool PropagatesOnChain => false; // no need to carry forward

        private readonly string label;

        public DebugLogHitEffect(EffectPhase phase, string label)
        {
            Phase = phase;
            this.label = label;
        }

        public void Apply(HitContext ctx, IHitResolver resolver)
        {
            // DamageDealt shows whether the Application damage effect has run yet.
            // If this is a Modifier and prints DamageDealt=0, ordering is correct.
            Debug.Log($"[DebugLogEffect] '{label}' running in phase {Phase} " +
                      $"| DamageDealt so far = {ctx.DamageDealt:F1} " +
                      $"| target = {(ctx.Target as MonoBehaviour)?.name}");
        }
    }
}
