using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Feedback
{
    // Owns active rolling accumulator numbers, GROUPED PER TARGET so all
    // operations (find, count, repack) touch only one enemy's short list rather
    // than scanning every accumulator in the scene — scales to many enemies.
    //
    // Within a target, numbers fold by effect key (one climbing number per
    // StatusSO) and lay out in a cumulative-height column ordered by accumulated
    // total (BIGGEST AT THE BOTTOM). Numbers slide smoothly to their slots, so
    // when totals change and the order shifts, they glide rather than snap.
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

        // Cumulative-height column ordered by accumulated total, biggest at the
        // bottom (slot 0). Numbers slide to these offsets. Local to one target.
        private static readonly List<AccumulatorNumber> sortBuffer = new List<AccumulatorNumber>();
        private void Layout(TargetGroup group)
        {
            sortBuffer.Clear();
            foreach (var kv in group.byEffect)
                sortBuffer.Add(kv.Value);
            // biggest total first -> placed at the bottom (lowest y)
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