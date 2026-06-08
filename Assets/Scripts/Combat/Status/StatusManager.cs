using System.Collections.Generic;
using UnityEngine;

namespace Combat.Status
{
    // Central ticker for all active status instances across all targets.
    // The ONLY thing that ticks statuses. Uses deferred add/remove queues so the
    // tick loop never mutates its collection mid-iteration — which is what makes
    // it safe when a tick kills a target (cleanup) or triggers a chain that
    // applies new statuses (addition) DURING the loop. (#2/#3/#4)
    public class StatusManager : MonoBehaviour
    {
        public static StatusManager Instance { get; private set; }

        private readonly List<StatusInstance> active = new List<StatusInstance>();
        private readonly List<StatusInstance> toAdd = new List<StatusInstance>();
        private readonly List<StatusInstance> toRemove = new List<StatusInstance>();

        private bool ticking;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Register(StatusInstance instance)
        {
            // Deferred if we're mid-tick, immediate otherwise.
            if (ticking) toAdd.Add(instance);
            else active.Add(instance);
        }

        public void Unregister(StatusInstance instance)
        {
            if (ticking) toRemove.Add(instance);
            else active.Remove(instance);
        }

        private void Update()
        {
            float dt = Time.deltaTime; // scaled time (#6) — respects slow-mo/pause

            ticking = true;
            for (int i = 0; i < active.Count; i++)
            {
                var inst = active[i];
                inst.Tick(dt);
                if (inst.Expired)
                    toRemove.Add(inst);
            }
            ticking = false;

            // process deferred mutations after the loop
            if (toRemove.Count > 0)
            {
                foreach (var inst in toRemove)
                    active.Remove(inst);
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
