using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

namespace GOAPGettingStarted.Sensors
{
    [GoapId("c3d4e5f6-a7b8-9012-cdef-123456789012")]
    public class PlayerVisibleSensor : LocalWorldSensorBase
    {
        public float DetectionRange = 25f;
        public float FieldOfViewAngle = 120f;

        private Transform player;
        private int raycastMask;

        public override void Created()
        {
            raycastMask = LayerMask.GetMask("Default", "Player");
        }

        public override void Update()
        {
            if (player == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                    player = playerObj.transform;
            }
        }

        public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
        {
            if (player == null)
                return new SenseValue(0);

            var toPlayer = player.position - agent.Transform.position;
            var distance = toPlayer.magnitude;

            if (distance > DetectionRange)
                return new SenseValue(0);

            if (Vector3.Angle(agent.Transform.forward, toPlayer) > FieldOfViewAngle * 0.5f)
                return new SenseValue(0);

            if (Physics.Raycast(agent.Transform.position + Vector3.up * 1.5f, toPlayer.normalized, out var hit, distance, raycastMask))
            {
                if (!hit.transform.CompareTag("Player"))
                    return new SenseValue(0);
            }

            return new SenseValue(1);
        }
    }
}