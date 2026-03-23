using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Behaviours;
using UnityEngine;
namespace GOAPGettingStarted.Sensors
{
    [GoapId("e6f7a8b9-c0d1-2345-fabc-789012345678")]
    public class LocationInvestigatedSensor : LocalWorldSensorBase
    {
        public override void Created() { }
        public override void Update() { }
        public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
        {
            return new SenseValue(0);
        }
    }
}