using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;
using GOAPGettingStarted.Behaviours;

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
            // TRUE WANDER: only resume the existing target while the agent is actively
            // walking to it AND we haven't been told to re-roll. WanderMarker (set on
            // the agent's GameObject) carries a "pick fresh" flag that WanderAction.Start
            // raises on every new wander entry. So each fresh wander — including after a
            // chase, an evade, or a pool respawn — picks a NEW point, instead of
            // resuming the held one (which read as accidental patrol behaviour).
            //
            // While mid-walk (flag down), we still hold the target so the agent commits
            // to a point instead of re-rolling every frame.
            var marker = GetMarker(agent);

            bool forceReroll = marker != null && marker.PickFreshTarget;
            if (marker != null) marker.PickFreshTarget = false;   // consume the flag

            if (!forceReroll && existingTarget is PositionTarget existing)
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

        private static WanderMarker GetMarker(IActionReceiver agent)
        {
            // The agent's Transform is the enemy root; the marker lives there.
            return agent.Transform != null
                ? agent.Transform.GetComponent<WanderMarker>()
                : null;
        }

        private Vector3 GetRandomNavMeshPosition(Vector3 origin)
        {
            for (int i = 0; i < 5; i++)
            {
                var randomCircle = Random.insideUnitCircle * 20f;
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