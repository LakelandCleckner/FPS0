using UnityEngine;
using Combat.Spawning;

namespace GOAPGettingStarted.Behaviours
{
    // A tiny flag component the wander system uses to force a fresh target pick. It
    // exists because the sensor is a GOAP sensor (no MonoBehaviour lifecycle of its
    // own) and the action's Data isn't reachable from the sensor — so the "pick fresh"
    // signal needs a shared place both can touch. The agent's GameObject is that place.
    //
    // Set PickFreshTarget = true whenever wander should re-roll instead of resuming:
    //   - WanderAction.Start raises it on every new wander entry (true wander).
    //   - OnSpawn raises it so a POOLED respawn wanders somewhere new, and clears any
    //     stale flag state. This is also the hook a future PATROL system resets through
    //     — patrol progress is agent state that must clear on death/respawn for waves
    //     of guards, and IPoolable.OnSpawn is where that reset lives.
    //
    // The sensor consumes (reads then lowers) the flag, so it forces exactly one fresh
    // pick per raise, not a continuous re-roll.
    public class WanderMarker : MonoBehaviour, IPoolable
    {
        public bool PickFreshTarget;

        public void OnSpawn()
        {
            // Reused enemy: force the next wander to pick a brand-new point rather than
            // resuming whatever it held when it died.
            PickFreshTarget = true;
        }

        public void OnDespawn() { }
    }
}
