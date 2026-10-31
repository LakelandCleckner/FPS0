using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using Combat.Enemies;
using UnityEngine;

namespace GOAPGettingStarted.Sensors
{
    // 1 while the player is within attack_range, or while a swing is still playing out.
    [GoapId("cddae3fd-9c23-4103-8920-0eb8333acd8f")]
    public class PlayerInAttackRangeSensor : LocalWorldSensorBase
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

        public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
        {
            var melee = agent.Transform.GetComponent<EnemyMeleeAttack>();
            if (melee == null || player == null) return new SenseValue(0);
            if (melee.IsSwinging) return new SenseValue(1);

            Vector3 to = player.position - agent.Transform.position;
            to.y = 0f;
            return new SenseValue(to.magnitude <= melee.Range ? 1 : 0);
        }
    }
}
