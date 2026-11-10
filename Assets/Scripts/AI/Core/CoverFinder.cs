using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Combat.AI
{
    // Finds cover at runtime from the NavMesh and level geometry: NavMesh edge points beside obstacles
    // that block the threat's line of sight. Points are reserved so two enemies don't share one.
    public static class CoverFinder
    {
        private const int Directions = 12;
        private static readonly float[] Rings = { 4f, 8f, 12f };
        private const float StandHeight = 1.2f;
        private const float ThreatEyeHeight = 1.6f;
        private const float ReserveRadius = 1.5f;

        private static readonly Dictionary<object, Vector3> reserved = new Dictionary<object, Vector3>();
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

        private static bool TakenByOther(Vector3 p, object owner)
        {
            foreach (var kv in reserved)
                if (kv.Key != owner && (kv.Value - p).sqrMagnitude < ReserveRadius * ReserveRadius) return true;
            return false;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
