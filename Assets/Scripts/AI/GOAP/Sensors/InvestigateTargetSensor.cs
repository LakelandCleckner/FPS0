using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Behaviours;
using UnityEngine;
namespace GOAPGettingStarted.Sensors
{
    [GoapId("c3d4e5f6-a7b8-9012-cdef-012345678901")]
    public class InvestigateTargetSensor : LocalTargetSensorBase
    {
        public override void Created() { }
        public override void Update() { }
        public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
        {
            var memory = agent.Transform.GetComponent<EnemyMemory>();
            if (memory == null || !memory.HasLastKnownPosition) return existingTarget;
            var position = memory.LastKnownPlayerPosition;
            if (existingTarget is PositionTarget pt) return pt.SetPosition(position);
            return new PositionTarget(position);
        }
    }
}