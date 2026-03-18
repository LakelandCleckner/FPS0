using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace GOAPGettingStarted.Sensors
{
    [GoapId("31e31669-cdfc-4c67-a721-9d935f55ad27")]
    public class WanderTargetSensor : LocalTargetSensorBase
    {
        public float MinPickDistance = 2f;

        public override void Created() { }
        public override void Update() { }

        public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
        {
            if (existingTarget is PositionTarget existing)
            {
                var dist = Vector3.Distance(agent.Transform.position, existing.Position);
                if (dist > MinPickDistance)
                    return existing;
            }

            var position = GetRandomNavMeshPosition(agent.Transform.position);

            if (existingTarget is PositionTarget pt)
                return pt.SetPosition(position);

            return new PositionTarget(position);
        }

        private Vector3 GetRandomNavMeshPosition(Vector3 origin)
        {
            for (int i = 0; i < 5; i++)
            {
                var randomCircle = Random.insideUnitCircle * 8f;
                var candidate = origin + new Vector3(randomCircle.x, 0f, randomCircle.y);

                if (NavMesh.SamplePosition(candidate, out var hit, 3f, NavMesh.AllAreas))
                {
                    // Force Y to match agent so GOAP's 3D distance check doesn't
                    // fail due to terrain height differences
                    var p = hit.position;
                    p.y = origin.y;
                    return p;
                }
            }

            return origin;
        }
    }
}