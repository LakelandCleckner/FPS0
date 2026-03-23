using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

namespace GOAPGettingStarted.Sensors
{
    [GoapId("d4e5f6a7-b8c9-0123-defa-234567890123")]
    public class PlayerTargetSensor : LocalTargetSensorBase
    {
        private Transform player;

        public override void Created() { }

        public override void Update()
        {
            if (player == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                    player = playerObj.transform;
            }
        }

        public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
        {
            if (player == null)
                return existingTarget;

            // Live TransformTarget so the agent tracks the player continuously.
            // PlayerVisibleSensor handles dropping the chase when LOS breaks
            // or the player moves out of DetectionRange.
            if (existingTarget is TransformTarget transformTarget)
                return transformTarget;

            return new TransformTarget(player);
        }
    }
}