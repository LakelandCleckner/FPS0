using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Status
{
    // Lives on each enemy/target. Owns that target's active status instances,
    // applies new ones (with dying-guard), and CLEANS UP from the central
    // manager on death — preventing orphaned ticks on a destroyed target (#2).
    //
    // Stage 1: just holds + applies + cleans up. Stacking and category counters
    // arrive in stage 2.
    [RequireComponent(typeof(EnemyHealth))]
    public class StatusReceiver : MonoBehaviour
    {
        private ITargetInfo target;
        private readonly List<StatusInstance> myStatuses = new List<StatusInstance>();

        private void Awake()
        {
            target = GetComponent<EnemyHealth>();
        }

        public void Apply(StatusInstance instance)
        {
            if (target == null || target.IsDying) return; // dying-guard (#1)

            myStatuses.Add(instance);
            StatusManager.Instance.Register(instance);
        }

        private void OnDisable()
        {
            // Death/despawn cleanup — pull all our statuses out of the manager
            // so it never ticks instances pointing at a gone target (#2).
            if (StatusManager.Instance == null) return;

            foreach (var inst in myStatuses)
                StatusManager.Instance.Unregister(inst);

            myStatuses.Clear();
        }
    }
}
