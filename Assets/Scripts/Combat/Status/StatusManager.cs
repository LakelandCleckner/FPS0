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
    // STAGE 2: ticks pools (was StatusInstances).
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
            if (ticking) toAdd.Add(pool);
            else active.Add(pool);
        }

        public void Unregister(EffectStackPool pool)
        {
            if (ticking) toRemove.Add(pool);
            else active.Remove(pool);
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
                    // tell the receiver to drop its reference too
                    (pool.Target as MonoBehaviour)?.GetComponent<StatusReceiver>()?.OnPoolExpired(pool);
                }
            }
            ticking = false;

            if (toRemove.Count > 0)
            {
                foreach (var p in toRemove) active.Remove(p);
                toRemove.Clear();
            }
            if (toAdd.Count > 0)
            {
                active.AddRange(toAdd);
                toAdd.Clear();
            }
        }
    }
}