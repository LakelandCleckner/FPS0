using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Runtime state of one active status on one target. Plain C# object (no
    // MonoBehaviour) for performance; structured so it can be pooled later
    // without rearchitecting (Reset re-initializes all fields).
    //
    // Ticks on an interval measured in scaled time. Tick damage routes through
    // the resolver so feedback fires and chains can trigger. Tick-on-apply is
    // the default (fires one tick immediately when applied).
    public class StatusInstance
    {
        public ITargetInfo Target;
        public IHitResolver Resolver;

        // snapshot taken at application time — ticks always use this, never live source
        public StatBlock Stats;
        public DamageSpec TickSpec;
        public List<IHitEffect> CarriedEffects; // for future chain propagation
        public int SourceFaction;

        public float RemainingDuration;
        public float TickInterval;

        // how this instance applies damage to its concrete target (set by applier)
        public System.Action<float, DamageType> ApplyDamage;

        private float tickAccumulator;
        public bool Expired { get; private set; }

        public void Init(
            ITargetInfo target,
            IHitResolver resolver,
            StatBlock stats,
            DamageSpec tickSpec,
            List<IHitEffect> carriedEffects,
            int sourceFaction,
            float duration,
            float tickInterval,
            System.Action<float, DamageType> applyDamage)
        {
            Target = target;
            Resolver = resolver;
            Stats = stats;
            TickSpec = tickSpec;
            CarriedEffects = carriedEffects;
            SourceFaction = sourceFaction;
            RemainingDuration = duration;
            TickInterval = Mathf.Max(0.01f, tickInterval); // clamp (#8)
            ApplyDamage = applyDamage;

            tickAccumulator = 0f;
            Expired = false;

            // tick-on-apply default
            DoTick();
        }

        // Called by the manager each frame with scaled delta. Returns true while
        // still alive, false when expired (manager then removes it).
        public void Tick(float scaledDelta)
        {
            if (Expired) return;

            RemainingDuration -= scaledDelta;
            tickAccumulator += scaledDelta;

            // fire as many interval ticks as accumulated this frame (handles
            // low frame rates without losing ticks)
            while (tickAccumulator >= TickInterval && !Expired)
            {
                tickAccumulator -= TickInterval;
                DoTick();
            }

            if (RemainingDuration <= 0f)
                Expired = true;
            // NOTE: no partial tick on expiry by design (#5)
        }

        private void DoTick()
        {
            if (Target == null || Target.IsDying) { Expired = true; return; }

            float raw = TickSpec.ComputeRaw(Stats, Target);
            // status ticks don't apply chain falloff here (depth 0 origin);
            // resistance is applied at the target's damage chokepoint.
            ApplyDamage?.Invoke(raw, TickSpec.Type);
        }

        // For pooling later: wipe references so a reused instance holds no stale state.
        public void Reset()
        {
            Target = null;
            Resolver = null;
            CarriedEffects = null;
            ApplyDamage = null;
            Expired = true;
        }
    }
}
