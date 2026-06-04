using UnityEngine;
using Combat.Core;

namespace Combat.Effects
{
    // Abstract base for all effect definitions authored as ScriptableObject
    // assets. The SO is a stateless RECIPE; it produces an IHitEffect.
    //
    // CACHING MODEL: effects are stateless by contract (all per-hit data lives
    // in HitContext, all per-target state lives in status objects on the enemy),
    // so the produced IHitEffect can be safely cached and shared across every
    // shot and every enemy. GetInstance() returns the cached singleton -> zero
    // per-shot allocation.
    //
    // Rare opt-out: an effect that genuinely needs mutable state ON ITSELF can
    // override RequiresFreshInstance to true and will be rebuilt each call.
    public abstract class HitEffectSO : ScriptableObject
    {
        private IHitEffect cached;

        protected virtual bool RequiresFreshInstance => false;

        protected abstract IHitEffect Build();

        public IHitEffect GetInstance()
        {
            if (RequiresFreshInstance)
                return Build();

            if (cached == null)
                cached = Build();

            return cached;
        }

        protected virtual void OnValidate()
        {
            cached = null; // tweaks during play-mode testing take effect
        }
    }
}
