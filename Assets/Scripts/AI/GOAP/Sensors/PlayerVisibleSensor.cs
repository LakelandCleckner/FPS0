using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Behaviours;

namespace GOAPGettingStarted.Sensors
{
    // PlayerVisible = the brain is tracking the player. The brain owns the sight check,
    // so GOAP and the goal state machine always agree.
    [GoapId("c3d4e5f6-a7b8-9012-cdef-123456789012")]
    public class PlayerVisibleSensor : LocalWorldSensorBase
    {
        public override void Created() { }
        public override void Update() { }

        public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
        {
            var brain = references.GetCachedComponent<AgentBrain>();
            return new SenseValue(brain != null && brain.IsTracking ? 1 : 0);
        }
    }
}
