using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

namespace GOAPGettingStarted.Sensors
{
    [GoapId("e8f9a0b1-c2d3-4567-bcde-678901234567")]
    public class PlayerEngagedSensor : LocalWorldSensorBase
    {
        public override void Created() { }
        public override void Update() { }

        public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
        {
            return new SenseValue(0);
        }
    }
}
