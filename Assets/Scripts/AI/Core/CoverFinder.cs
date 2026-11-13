using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Combat.AI
{
    // A spot to fight from cover: hide at Point, shoot from Peek (the same spot for half cover).
    public struct CoverSpot
    {
        public Vector3 Point;
        public Vector3 Peek;
        // Crouch to hide, stand to shoot; otherwise stand behind it and step out to Peek
        public bool Half;
    }

    // Everything a combat cover search needs.
    public struct CoverQuery
    {
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
    }

    // Finds cover at runtime from the NavMesh and level geometry: NavMesh edge points beside obstacles
    // that block the threat's line of sight. Points are reserved so two enemies don't share one.
    public static class CoverFinder
    {
        private const int Directions = 12;
        private static readonly float[] Rings = { 4f, 8f, 12f };
        private const float StandHeight = 1.2f;
        private const float ThreatEyeHeight = 1.6f;
        private const float ReserveRadius = 1.5f;

        // Combat cover: hidden crouched below this, hidden standing below HeadHeight
        private const float CrouchHeight = 0.9f;
        private const float HeadHeight = 1.6f;
        private static readonly float[] CombatRings = { 3f, 6f, 10f, 14f };
        private const int CombatDirections = 10;
        private const int ThreatDirections = 12;
        private const float EdgeReach = 3f;
        private const float SameSpot = 0.6f;
        private static readonly float[] PeekSteps = { 1.1f, -1.1f, 1.7f, -1.7f };
        private const float HalfCoverBonus = 0.05f;

        private static readonly Dictionary<object, Vector3> reserved = new Dictionary<object, Vector3>();
        private static readonly List<Vector3> candidates = new List<Vector3>(64);
        private static int geometryMask = -1;

        private static int GeometryMask
        {
            get
            {
                if (geometryMask == -1) geometryMask = LayerMask.GetMask("Default", "Ground");
                return geometryMask;
            }
        }

        // Best cover within radius; false when nothing blocks the threat.
        public static bool Find(Vector3 from, Vector3 threat, float radius, float minThreatDistance, object owner, out Vector3 cover)
        {
            cover = from;
            float best = float.MaxValue;
            bool found = false;
            Vector3 threatEye = threat + Vector3.up * ThreatEyeHeight;

            for (int r = 0; r < Rings.Length; r++)
            {
                float ring = Mathf.Min(Rings[r], radius);
                for (int d = 0; d < Directions; d++)
                {
                    float a = (d + r * 0.5f) * Mathf.PI * 2f / Directions;
                    Vector3 sample = from + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ring;
                    if (!NavMesh.SamplePosition(sample, out var onMesh, 2f, NavMesh.AllAreas)) continue;
                    if (!NavMesh.FindClosestEdge(onMesh.position, out var edge, NavMesh.AllAreas)) continue;

                    Vector3 p = edge.position;
                    if ((p - from).sqrMagnitude > radius * radius) continue;
                    float threatDist = Vector3.Distance(Flat(p), Flat(threat));
                    if (threatDist < minThreatDistance) continue;
                    if (!Hidden(p, threatEye)) continue;
                    if (TakenByOther(p, owner)) continue;

                    // Nearer is better; cover that also puts more distance on the threat breaks ties.
                    float score = Vector3.Distance(from, p) - threatDist * 0.25f;
                    if (score < best) { best = score; cover = p; found = true; }
                }
            }

            if (found) reserved[owner] = cover;
            return found;
        }

        // Best cover to fight from inside the range band; reserves it.
        public static bool FindCombat(in CoverQuery q, out CoverSpot best)
        {
            best = default;
            GatherEdges(q);

            float bestScore = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!Evaluate(q, candidates[i], out var spot, out float score) || score <= bestScore) continue;
                bestScore = score;
                best = spot;
                found = true;
            }

            if (found) reserved[q.Owner] = best.Point;
            else reserved.Remove(q.Owner);
            return found;
        }

        // The spot still hides from the threat, is in the band, and its peek still sees the threat
        public static bool StillCovers(in CoverSpot s, Vector3 threat, float minRange, float maxRange)
        {
            float d = Flat(s.Point - threat).magnitude;
            if (d < minRange || d > maxRange) return false;
            Vector3 eye = EyeOf(threat);
            if (!Blocked(eye, s.Point + Vector3.up * CrouchHeight)) return false;
            if (!s.Half && !Blocked(eye, s.Point + Vector3.up * HeadHeight)) return false;
            return PositionScorer.HasLineOfSight(s.Peek, threat);
        }

        // Straight away from the threat, on the NavMesh.
        public static Vector3 AwayFrom(Vector3 from, Vector3 threat, float distance)
        {
            Vector3 dir = Flat(from - threat);
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            Vector3 p = from + dir.normalized * distance;
            return NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas) ? hit.position : from;
        }

        // True while the threat can't see a standing enemy at this point.
        public static bool Hidden(Vector3 point, Vector3 threatEye)
            => Physics.Linecast(threatEye, point + Vector3.up * StandHeight, GeometryMask, QueryTriggerInteraction.Ignore);

        public static Vector3 EyeOf(Vector3 threat) => threat + Vector3.up * ThreatEyeHeight;

        public static void Release(object owner) => reserved.Remove(owner);

        // Edge points near the enemy and on a ring at the preferred range around the threat
        private static void GatherEdges(in CoverQuery q)
        {
            candidates.Clear();
            if (q.HasCurrent) candidates.Add(q.Current);

            for (int r = 0; r < CombatRings.Length; r++)
            {
                if (CombatRings[r] > q.SearchRadius) break;
                for (int d = 0; d < CombatDirections; d++)
                {
                    float a = (d + r * 0.5f) * Mathf.PI * 2f / CombatDirections;
                    AddEdge(q.From + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * CombatRings[r]);
                }
            }

            Vector3 fromThreat = Flat(q.From - q.Threat);
            float baseAngle = fromThreat.sqrMagnitude > 0.01f ? Mathf.Atan2(fromThreat.x, fromThreat.z) : 0f;
            for (int d = 0; d < ThreatDirections; d++)
            {
                float a = baseAngle + (d - ThreatDirections / 2) * Mathf.PI * 2f / ThreatDirections;
                Vector3 p = q.Threat + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * q.PreferredRange;
                if (Flat(p - q.From).sqrMagnitude <= q.SearchRadius * q.SearchRadius) AddEdge(p);
            }
        }

        private static void AddEdge(Vector3 sample)
        {
            if (!NavMesh.SamplePosition(sample, out var onMesh, 2f, NavMesh.AllAreas)) return;
            if (!NavMesh.FindClosestEdge(onMesh.position, out var edge, NavMesh.AllAreas) || edge.distance > EdgeReach) return;
            Vector3 p = edge.position;
            for (int i = 0; i < candidates.Count; i++)
                if (Flat(candidates[i] - p).sqrMagnitude < SameSpot * SameSpot) return;
            candidates.Add(p);
        }

        // Half cover: hidden crouched, sees the threat standing. Full cover: hidden standing, with a peek spot beside it.
        private static bool Evaluate(in CoverQuery q, Vector3 p, out CoverSpot spot, out float score)
        {
            spot = default;
            score = 0f;
            float dist = Flat(p - q.Threat).magnitude;
            if (dist < q.MinRange || dist > q.MaxRange) return false;
            if (Flat(p - q.From).magnitude > q.SearchRadius + 1f) return false;
            if (TakenByOther(p, q.Owner)) return false;

            Vector3 eye = EyeOf(q.Threat);
            if (!Blocked(eye, p + Vector3.up * CrouchHeight)) return false;

            spot.Point = p;
            if (!Blocked(eye, p + Vector3.up * HeadHeight))
            {
                if (!PositionScorer.HasLineOfSight(p, q.Threat)) return false;
                spot.Half = true;
                spot.Peek = p;
            }
            else if (!FindPeek(q, p, out spot.Peek)) return false;

            var w = q.Weights;
            float band = Mathf.Max(1f, q.MaxRange - q.MinRange);
            score -= w.range * Mathf.Abs(dist - q.PreferredRange) / band;
            score -= w.travel * Flat(p - q.From).magnitude / Mathf.Max(1f, q.SearchRadius);
            score -= w.allySpacing * AlliesNear(q, p);

            if (w.exposure != 0f)
            {
                Vector3 toPoint = Flat(p - q.Threat);
                Vector3 view = Flat(q.ThreatViewForward);
                if (toPoint.sqrMagnitude > 0.01f && view.sqrMagnitude > 0.01f)
                    score -= w.exposure * Mathf.Max(0f, Vector3.Dot(view.normalized, toPoint.normalized));
            }

            if (q.HasCurrent && Flat(p - q.Current).sqrMagnitude < SameSpot * SameSpot) score += w.keepCurrent;
            if (spot.Half) score += HalfCoverBonus;
            return true;
        }

        // A step to either side of the cover, across the line to the threat, that sees the threat
        private static bool FindPeek(in CoverQuery q, Vector3 p, out Vector3 peek)
        {
            peek = p;
            Vector3 toThreat = Flat(q.Threat - p);
            if (toThreat.sqrMagnitude < 0.01f) return false;
            Vector3 side = Vector3.Cross(Vector3.up, toThreat.normalized);

            for (int i = 0; i < PeekSteps.Length; i++)
            {
                if (!NavMesh.SamplePosition(p + side * PeekSteps[i], out var hit, 0.6f, NavMesh.AllAreas)) continue;
                if (!NavMesh.Raycast(p, hit.position, out _, NavMesh.AllAreas) && PositionScorer.HasLineOfSight(hit.position, q.Threat))
                {
                    peek = hit.position;
                    return true;
                }
            }
            return false;
        }

        private static int AlliesNear(in CoverQuery q, Vector3 p)
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
            return n;
        }

        private static bool Blocked(Vector3 from, Vector3 to)
            => Physics.Linecast(from, to, GeometryMask, QueryTriggerInteraction.Ignore);

        private static bool TakenByOther(Vector3 p, object owner)
        {
            foreach (var kv in reserved)
                if (kv.Key != owner && (kv.Value - p).sqrMagnitude < ReserveRadius * ReserveRadius) return true;
            return false;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
