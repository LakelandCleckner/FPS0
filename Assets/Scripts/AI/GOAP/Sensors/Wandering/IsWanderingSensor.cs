using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

namespace GOAPGettingStarted.Sensors
{
    [GoapId("a18100f0-94b7-4b7d-82b9-39a9599f015a")]
    public class IsWanderingSensor : LocalWorldSensorBase
    {
        public override void Created() { }
        public override void Update() { }

        public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
        {
            // Always returns 0 so the wander goal is never satisfied,
            // keeping GOAP continuously picking WanderAction.
            return new SenseValue(0);
        }
    }
}