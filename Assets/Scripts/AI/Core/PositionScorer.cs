using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Combat.AI
{
    // How much each factor counts when scoring a point. One set per purpose (fire now; cover, retreat, search later).
    [Serializable]
    public struct PointWeights
    {
        [Tooltip("Penalty per range-band fraction away from the preferred distance.")]
        public float range;
        [Tooltip("Penalty for distance from the enemy's current position.")]
        public float travel;
        [Tooltip("Penalty per ally standing or reserved near the point.")]
        public float allySpacing;
        [Tooltip("Bonus for a point next to a wall or obstacle.")]
        public float nearCover;
        [Tooltip("Penalty for points inside the target's view direction.")]
        public float exposure;
        [Tooltip("Bonus for keeping the current point (stops flip-flopping).")]
        public float keepCurrent;

        public static PointWeights Fire => new PointWeights
        {
            range = 1f, travel = 0.4f, allySpacing = 1f, nearCover = 0.4f, exposure = 0.3f, keepCurrent = 0.6f
        };

        // Combat cover: nearCover is unused (every candidate is cover)
        public static PointWeights Cover => new PointWeights
        {
            range = 1f, travel = 0.6f, allySpacing = 1f, nearCover = 0f, exposure = 0.3f, keepCurrent = 0.8f
        };
    }

    public enum PointPurpose { Fire }

    // One candidate from the last evaluation, kept for the debug overlay.
    public struct ScoredPoint
    {
        public Vector3 Position;
        public float Score;
        public bool Rejected;
    }

    // Everything a point search needs.
    public struct PointQuery
    {
        public PointPurpose Purpose;
        public object Owner;
        public Transform Self;
        public Vector3 From;
        public Vector3 Threat;
        public Vector3 ThreatViewForward;
        public float MinRange;
        public float PreferredRange;
        public float MaxRange;
        public float SearchRadius;
        public float AllySpacing;
        public PointWeights Weights;
        public bool HasCurrent;
        public Vector3 Current;
        public Collider Area;
        // Squad flankers: bonus for points off the threat in this direction (zero = none)
        public Vector3 FlankDir;
    }

    // Weighted NavMesh point choice with a per-frame evaluation cap; callers keep their old point and retry next frame.
    public static class PositionScorer
    {
        public const int MaxEvaluationsPerFrame = 2;
        public const float EyeHeight = 1.5f;
        public const float TargetHeight = 1.2f;

        private static readonly float[] Rings = { 3f, 6f, 10f, 14f };
        private const int RingDirections = 10;
        private const int ThreatDirections = 12;
        private const float CoverEdgeDistance = 1.2f;
        public const float FlankWeight = 0.8f;

        private static readonly Dictionary<object, Vector3> reserved = new Dictionary<object, Vector3>();
        private static readonly List<Vector3> candidates = new List<Vector3>(64);
        private static int frame = -1;
        private static int evaluations;
        private static int geometryMask = -1;

        public static int RaysThisFrame { get; private set; }

        private static int GeometryMask
        {
            get
            {
                if (geometryMask == -1) geometryMask = LayerMask.GetMask("Default", "Ground");
                return geometryMask;
            }
        }

        // False once this frame's budget is spent
        public static bool TryBeginEvaluation()
        {
            Tick();
            if (evaluations >= MaxEvaluationsPerFrame) return false;
            evaluations++;
            return true;
        }

        public static bool HasLineOfSight(Vector3 point, Vector3 threat)
        {
            Tick();
            RaysThisFrame++;
            return !Physics.Linecast(point + Vector3.up * EyeHeight, threat + Vector3.up * TargetHeight,
                                     GeometryMask, QueryTriggerInteraction.Ignore);
        }

        public static void Release(object owner) => reserved.Remove(owner);

        // How well a point sits on the flank side of the threat
        public static float FlankBonus(Vector3 flankDir, Vector3 threat, Vector3 p)
        {
            if (flankDir.sqrMagnitude < 0.01f) return 0f;
            Vector3 off = Flat(p - threat);
            return off.sqrMagnitude > 0.01f ? FlankWeight * Vector3.Dot(off.normalized, flankDir) : 0f;
        }

        public static bool TryGetReserved(object owner, out Vector3 point) => reserved.TryGetValue(owner, out point);

        // Best point for the query; record (optional) receives every candidate for debugging.
        public static bool Find(in PointQuery q, out Vector3 best, List<ScoredPoint> record = null)
        {
            best = q.From;
            record?.Clear();
            Gather(q);

            float bestScore = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 p = candidates[i];
                bool ok = Score(q, p, out float score);
                record?.Add(new ScoredPoint { Position = p, Score = score, Rejected = !ok });
                if (!ok || score <= bestScore) continue;
                bestScore = score;
                best = p;
                found = true;
            }

            if (found) reserved[q.Owner] = best;
            else reserved.Remove(q.Owner);
            return found;
        }

        // Rings around the enemy, a ring around the threat at the preferred range, and the current point
        private static void Gather(in PointQuery q)
        {
            candidates.Clear();
            if (q.HasCurrent) candidates.Add(q.Current);

            for (int r = 0; r < Rings.Length; r++)
            {
                if (Rings[r] > q.SearchRadius) break;
                for (int d = 0; d < RingDirections; d++)
                {
                    float a = (d + r * 0.5f) * Mathf.PI * 2f / RingDirections;
                    AddSample(q.From + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Rings[r]);
                }
            }

            Vector3 fromThreat = Flat(q.From - q.Threat);
            float baseAngle = fromThreat.sqrMagnitude > 0.01f ? Mathf.Atan2(fromThreat.x, fromThreat.z) : 0f;
            for (int d = 0; d < ThreatDirections; d++)
            {
                float a = baseAngle + (d - ThreatDirections / 2) * Mathf.PI * 2f / ThreatDirections;
                Vector3 p = q.Threat + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * q.PreferredRange;
                if (Flat(p - q.From).sqrMagnitude <= q.SearchRadius * q.SearchRadius) AddSample(p);
            }
        }

        private static void AddSample(Vector3 p)
        {
            if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) candidates.Add(hit.position);
        }

        private static bool Score(in PointQuery q, Vector3 p, out float score)
        {
            score = 0f;
            float dist = Flat(p - q.Threat).magnitude;
            if (dist < q.MinRange || dist > q.MaxRange) return false;
            if (q.Area != null && (q.Area.ClosestPoint(p) - p).sqrMagnitude > 0.01f) return false;
            if (!HasLineOfSight(p, q.Threat)) return false;

            var w = q.Weights;
            float band = Mathf.Max(1f, q.MaxRange - q.MinRange);
            score -= w.range * Mathf.Abs(dist - q.PreferredRange) / band;
            score -= w.travel * Flat(p - q.From).magnitude / Mathf.Max(1f, q.SearchRadius);
            score -= w.allySpacing * AlliesNear(q, p);

            if (w.nearCover != 0f && NavMesh.FindClosestEdge(p, out var edge, NavMesh.AllAreas) && edge.distance <= CoverEdgeDistance)
                score += w.nearCover;

            if (w.exposure != 0f)
            {
                Vector3 toPoint = Flat(p - q.Threat);
                Vector3 view = Flat(q.ThreatViewForward);
                if (toPoint.sqrMagnitude > 0.01f && view.sqrMagnitude > 0.01f)
                    score -= w.exposure * Mathf.Max(0f, Vector3.Dot(view.normalized, toPoint.normalized));
            }

            if (q.HasCurrent && Flat(p - q.Current).sqrMagnitude < 1f) score += w.keepCurrent;
            score += FlankBonus(q.FlankDir, q.Threat, p);
            return true;
        }

        // Other enemies standing near the point, plus points other enemies have reserved
        private static int AlliesNear(in PointQuery q, Vector3 p)
        {
            float r2 = q.AllySpacing * q.AllySpacing;
            int n = 0;
            var all = EnemyBrain.Active;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || b.transform == q.Self) continue;
                if (Flat(b.transform.position - p).sqrMagnitude < r2) n++;
            }
            foreach (var kv in reserved)
                if (kv.Key != q.Owner && Flat(kv.Value - p).sqrMagnitude < r2) n++;
            return n;
        }

        private static void Tick()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            evaluations = 0;
            RaysThisFrame = 0;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
