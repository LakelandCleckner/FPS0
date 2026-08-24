using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Feedback
{
    // Owns active rolling accumulator numbers, grouped per target so operations touch
    // one enemy's short list rather than every accumulator in the scene. Within a
    // target, numbers fold by effect key and lay out biggest-at-the-bottom, sliding to
    // their slots.
    //
    // Pooled reuse: a pooled enemy is the same ICombatant across lives, so numbers can
    // bleed between lives. ClearTarget wipes a target's numbers on despawn/spawn, and a
    // generation check drops any number left from a previous life regardless of timing.
    public class DamageAccumulatorRegistry : MonoBehaviour
    {
        public static DamageAccumulatorRegistry Instance { get; private set; }

        [Header("Prefab & Pool")]
        [SerializeField] private AccumulatorNumber prefab;
        [SerializeField] private int initialSize = 16;

        [Header("Behaviour")]
        [Tooltip("Seconds of no new damage before a rolling number releases/fades.")]
        [SerializeField] private float releaseWindow = 0.7f;
        [SerializeField] private float releaseFadeTime = 0.3f;
        [Tooltip("Extra gap between stacked numbers, on top of their own heights.")]
        [SerializeField] private float columnGap = 0.1f;
        [Tooltip("How fast numbers slide to their column slot (higher = snappier).")]
        [SerializeField] private float slideSpeed = 10f;

        [Header("Sizing (matches floating-number config)")]
        [SerializeField] private float minLogDistance = -0.69f;
        [SerializeField] private float maxLogDistance = 0.69f;
        [SerializeField] private float minSize = 2.5f;
        [SerializeField] private float maxSize = 12f;

        private readonly Queue<AccumulatorNumber> pool = new Queue<AccumulatorNumber>();

        private class TargetGroup
        {
            public readonly Dictionary<object, AccumulatorNumber> byEffect
                = new Dictionary<object, AccumulatorNumber>();
        }
        private readonly Dictionary<ICombatant, TargetGroup> groups
            = new Dictionary<ICombatant, TargetGroup>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            for (int i = 0; i < initialSize; i++)
                pool.Enqueue(CreateOne());
        }

        private AccumulatorNumber CreateOne()
        {
            var n = Instantiate(prefab, transform);
            n.gameObject.SetActive(false);
            return n;
        }

        public void Release(AccumulatorNumber n)
        {
            if (groups.TryGetValue(n.Target, out var group))
            {
                group.byEffect.Remove(n.EffectKey);
                if (group.byEffect.Count == 0)
                    groups.Remove(n.Target);
                else
                    Layout(group);
            }
            pool.Enqueue(n);
        }

        public void Report(
            ICombatant target, object effectKey, Transform follow,
            float amount, DamageTypeSO type, bool isCrit, bool isDebuffed)
        {
            if (target == null || effectKey == null) return;

            // Evict numbers left from a previous life of this pooled target before
            // adding — a generation mismatch means the number belongs to a dead life.
            EvictStale(target);

            if (!groups.TryGetValue(target, out var group))
            {
                group = new TargetGroup();
                groups[target] = group;
            }

            if (group.byEffect.TryGetValue(effectKey, out var existing))
            {
                existing.Absorb(amount); // revives if mid-release; triggers repack
                return;
            }

            var n = pool.Count > 0 ? pool.Dequeue() : CreateOne();
            n.Begin(
                registry: this,
                target: target,
                effectKey: effectKey,
                follow: follow,
                type: type,
                isCrit: isCrit,
                isDebuffed: isDebuffed,
                releaseWindow: releaseWindow,
                releaseFadeTime: releaseFadeTime,
                slideSpeed: slideSpeed,
                minSize: minSize, maxSize: maxSize,
                minLog: minLogDistance, maxLog: maxLogDistance);

            group.byEffect[effectKey] = n;
            n.Absorb(amount);
            Layout(group);
        }

        public void RequestRepack(ICombatant target)
        {
            if (groups.TryGetValue(target, out var group))
                Layout(group);
        }

        // Immediately drop all accumulator numbers for a target — called when a pooled
        // enemy despawns/spawns so its numbers don't linger onto its next life.
        public void ClearTarget(ICombatant target)
        {
            if (target == null) return;
            if (!groups.TryGetValue(target, out var group)) return;

            foreach (var kv in group.byEffect)
            {
                var n = kv.Value;
                if (n != null)
                {
                    n.gameObject.SetActive(false);
                    pool.Enqueue(n);
                }
            }
            group.byEffect.Clear();
            groups.Remove(target);
        }

        // Drop any of a target's numbers whose generation no longer matches the target's
        // current life. Runs before each Report so a fresh hit on a reused enemy can't
        // absorb into a number from the previous life, even if a tick reported on the
        // same frame the previous life died.
        private static readonly List<AccumulatorNumber> staleBuffer = new List<AccumulatorNumber>();
        private void EvictStale(ICombatant target)
        {
            if (!groups.TryGetValue(target, out var group)) return;

            int currentGen = AccumulatorPoolReset.GetGeneration(target);

            staleBuffer.Clear();
            foreach (var kv in group.byEffect)
                if (kv.Value.Generation != currentGen)
                    staleBuffer.Add(kv.Value);

            for (int i = 0; i < staleBuffer.Count; i++)
            {
                var n = staleBuffer[i];
                group.byEffect.Remove(n.EffectKey);
                n.gameObject.SetActive(false);
                pool.Enqueue(n);
            }

            if (group.byEffect.Count == 0)
                groups.Remove(target);
        }

        private static readonly List<AccumulatorNumber> sortBuffer = new List<AccumulatorNumber>();
        private void Layout(TargetGroup group)
        {
            sortBuffer.Clear();
            foreach (var kv in group.byEffect)
                sortBuffer.Add(kv.Value);
            sortBuffer.Sort((a, b) => b.Total.CompareTo(a.Total));

            float y = 0f;
            for (int i = 0; i < sortBuffer.Count; i++)
            {
                var n = sortBuffer[i];
                n.SetColumnOffset(Vector3.up * y);
                y += n.Height + columnGap;
            }
        }
    }
}