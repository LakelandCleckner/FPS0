using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Central ticker for all active EffectStackPools across all targets. The ONLY
    // thing that ticks statuses. Deferred add/remove queues keep the tick loop
    // from mutating its collection mid-iteration — safe when a tick kills a target
    // (cleanup) or a chain applies a status DURING the loop.
    //
    // PERF: removal is SWAP-BACK. Pool order carries no meaning (every pool ticks
    // once per frame regardless of position), so paying List.Remove's linear search
    // plus element shuffle bought nothing. Also caches each pool's StatusReceiver at
    // registration instead of doing a cast + GetComponent per expiry.
    public class StatusManager : MonoBehaviour
    {
        public static StatusManager Instance { get; private set; }

        private readonly List<EffectStackPool> active = new List<EffectStackPool>();
        private readonly List<EffectStackPool> toAdd = new List<EffectStackPool>();
        private readonly List<EffectStackPool> toRemove = new List<EffectStackPool>();

        private bool ticking;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Register(EffectStackPool pool)
        {
            // Fallback only. StatusReceiver.Apply assigns Owner at construction, so
            // this normally does nothing. Kept so a pool registered by some future
            // path without a receiver still gets its expiry notification, matching
            // the original GetComponent behaviour exactly.
            if (pool.Owner == null)
                pool.Owner = (pool.Target as MonoBehaviour)?.GetComponent<StatusReceiver>();

            if (ticking) toAdd.Add(pool);
            else active.Add(pool);
        }

        public void Unregister(EffectStackPool pool)
        {
            if (ticking) toRemove.Add(pool);
            else SwapBackRemove(pool);
        }

        private void Update()
        {
            float dt = Time.deltaTime; // scaled — respects slow-mo/pause

            ticking = true;
            for (int i = 0; i < active.Count; i++)
            {
                var pool = active[i];
                pool.Tick(dt);
                if (pool.Expired)
                {
                    toRemove.Add(pool);
                    // Tell the receiver to drop its reference too.
                    //
                    // Deliberately `!= null` and NOT `?.` — the null-conditional
                    // operator uses reference null, bypassing Unity's overloaded
                    // equality, so it would happily invoke a method on a DESTROYED
                    // MonoBehaviour and throw MissingReferenceException. `!= null`
                    // calls Unity's operator and correctly treats destroyed as null.
                    if (pool.Owner != null)
                        pool.Owner.OnPoolExpired(pool);
                }
            }
            ticking = false;

            if (toRemove.Count > 0)
            {
                for (int i = 0; i < toRemove.Count; i++) SwapBackRemove(toRemove[i]);
                toRemove.Clear();
            }
            if (toAdd.Count > 0)
            {
                active.AddRange(toAdd);
                toAdd.Clear();
            }
        }

        // Order-independent removal: overwrite the slot with the last element and
        // clip the tail. O(n) find, O(1) removal, no shuffle.
        private void SwapBackRemove(EffectStackPool pool)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (!ReferenceEquals(active[i], pool)) continue;

                int last = active.Count - 1;
                active[i] = active[last];
                active.RemoveAt(last);
                return;
            }
        }
    }
}