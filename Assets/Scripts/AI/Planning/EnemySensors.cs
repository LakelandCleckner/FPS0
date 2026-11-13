using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.AI;

namespace Combat.AI.Goap
{
    // Every world and target key read straight from EnemyPerception and EnemyBrain.
    public class EnemySensors : MultiSensorBase
    {
        public EnemySensors()
        {
            AddLocalWorldSensor<HasTarget>((agent, refs) => refs.GetCachedComponent<EnemyPerception>().HasTarget);
            AddLocalWorldSensor<TargetVisible>((agent, refs) => refs.GetCachedComponent<EnemyPerception>().TargetVisible);
            AddLocalWorldSensor<HasLastKnown>((agent, refs) => refs.GetCachedComponent<EnemyPerception>().HasLastKnown);
            AddLocalWorldSensor<HasAttackToken>((agent, refs) => refs.GetCachedComponent<EnemyBrain>().HasToken);
            AddLocalWorldSensor<ShieldDown>((agent, refs) => refs.GetCachedComponent<EnemyBrain>().ShieldDown);
            AddLocalWorldSensor<TargetKilled>((agent, refs) => false);
            AddLocalWorldSensor<ShieldRecovered>((agent, refs) => false);
            AddLocalWorldSensor<Searched>((agent, refs) => false);
            AddLocalWorldSensor<IsIdle>((agent, refs) => false);
            AddLocalWorldSensor<InFiringPosition>((agent, refs) => refs.GetCachedComponent<EnemyBrain>().InFiringPosition);
            AddLocalWorldSensor<NeedsReload>((agent, refs) => refs.GetCachedComponent<EnemyBrain>().NeedsReload);
            AddLocalWorldSensor<HasRangedToken>((agent, refs) => refs.GetCachedComponent<EnemyBrain>().HasRangedToken);
            AddLocalWorldSensor<HasCombatCover>((agent, refs) => refs.GetCachedComponent<EnemyBrain>().HasCombatCover);
            AddLocalWorldSensor<InCover>((agent, refs) => refs.GetCachedComponent<EnemyBrain>().InCover);
            AddLocalWorldSensor<Defensive>((agent, refs) => (int)refs.GetCachedComponent<EnemyBrain>().Band);
            AddLocalWorldSensor<Awareness>((agent, refs) => (int)refs.GetCachedComponent<EnemyPerception>().Status);

            AddLocalTargetSensor<CombatTarget>((agent, refs, existing) =>
            {
                var p = refs.GetCachedComponent<EnemyPerception>();
                if (!p.HasTarget || p.Target == null) return null;
                if (existing is TransformTarget tt) return tt.SetTransform(p.Target);
                return new TransformTarget(p.Target);
            });

            AddLocalTargetSensor<LastKnownTarget>((agent, refs, existing) =>
            {
                var p = refs.GetCachedComponent<EnemyPerception>();
                if (!p.HasLastKnown) return null;
                if (existing is PositionTarget pt) return pt.SetPosition(p.LastKnownPosition);
                return new PositionTarget(p.LastKnownPosition);
            });

            AddLocalTargetSensor<CoverTarget>((agent, refs, existing) =>
            {
                var brain = refs.GetCachedComponent<EnemyBrain>();
                if (brain == null || !brain.TryGetCover(out var point)) return null;
                if (existing is PositionTarget pt) return pt.SetPosition(point);
                return new PositionTarget(point);
            });

            AddLocalTargetSensor<FiringTarget>((agent, refs, existing) =>
            {
                var brain = refs.GetCachedComponent<EnemyBrain>();
                if (brain == null || !brain.TryGetFiringPoint(out var point)) return null;
                if (existing is PositionTarget pt) return pt.SetPosition(point);
                return new PositionTarget(point);
            });

            AddLocalTargetSensor<CombatCoverTarget>((agent, refs, existing) =>
            {
                var brain = refs.GetCachedComponent<EnemyBrain>();
                if (brain == null || !brain.TryGetCombatCover(out var point)) return null;
                if (existing is PositionTarget pt) return pt.SetPosition(point);
                return new PositionTarget(point);
            });

            // Holds the current point while walking to it; rolls a new one when a wander starts.
            AddLocalTargetSensor<WanderTarget>((agent, refs, existing) =>
            {
                var brain = refs.GetCachedComponent<EnemyBrain>();
                bool reroll = brain != null && brain.WanderReroll;
                if (brain != null) brain.WanderReroll = false;

                Vector3 origin = agent.Transform.position;
                if (!reroll && existing is PositionTarget held && Vector3.Distance(origin, held.Position) > MinPickDistance)
                    return held;

                Vector3 point = RandomNavMeshPoint(origin);
                if (existing is PositionTarget pt) return pt.SetPosition(point);
                return new PositionTarget(point);
            });
        }

        private const float MinPickDistance = 2f;
        private const float WanderRadius = 20f;

        public override void Created() { }
        public override void Update() { }

        private static Vector3 RandomNavMeshPoint(Vector3 origin)
        {
            for (int i = 0; i < 5; i++)
            {
                var c = Random.insideUnitCircle * WanderRadius;
                if (NavMesh.SamplePosition(origin + new Vector3(c.x, 0f, c.y), out var hit, 3f, NavMesh.AllAreas))
                {
                    // Keep the agent's height so GOAP's distance check isn't thrown by terrain
                    var p = hit.position;
                    p.y = origin.y;
                    return p;
                }
            }
            return origin;
        }
    }
}
