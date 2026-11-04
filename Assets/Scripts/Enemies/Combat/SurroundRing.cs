using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Combat.Enemies
{
    // Sits on the player. Hands nearby enemies evenly spaced slots around the player so they surround instead of piling in.
    public class SurroundRing : MonoBehaviour
    {
        [Tooltip("Slot ring radius from the player's centre (m).")]
        [SerializeField] private float radius = 1.6f;
        [SerializeField] private int slots = 6;
        [Tooltip("Enemies closer than this take a slot (m).")]
        [SerializeField] private float engageDistance = 8f;
        [Tooltip("Ring for enemies beyond the slot count (m).")]
        [SerializeField] private float overflowRadius = 3f;
        [Tooltip("Seconds between slot reassignments.")]
        [SerializeField] private float reassignInterval = 0.25f;
        [Tooltip("Keep the current slot unless a free one is this many degrees closer to the enemy's bearing.")]
        [SerializeField] private float stickiness = 40f;

        [SerializeField] private bool drawGizmos = true;

        private readonly List<Transform> members = new List<Transform>();
        private readonly Dictionary<Transform, int> assigned = new Dictionary<Transform, int>();
        private readonly List<Transform> order = new List<Transform>();
        private bool[] taken = new bool[0];
        private float nextAssign;

        public float EngageDistance => engageDistance;

        public void Join(Transform enemy)
        {
            if (enemy == null || members.Contains(enemy)) return;
            members.Add(enemy);
            nextAssign = 0f;
        }

        public void Leave(Transform enemy)
        {
            if (enemy == null || !members.Remove(enemy)) return;
            assigned.Remove(enemy);
            nextAssign = 0f;
        }

        // World position of this enemy's slot on the navmesh; the player's position if it has none yet
        public Vector3 SlotPosition(Transform enemy)
        {
            if (!assigned.TryGetValue(enemy, out int s)) return transform.position;

            int ringCount = Mathf.Max(1, slots);
            bool overflow = s >= ringCount;
            int count = overflow ? ringCount * 2 : ringCount;
            int index = overflow ? s - ringCount : s;
            float r = overflow ? overflowRadius : radius;

            float angle = index * (360f / count) * Mathf.Deg2Rad;
            Vector3 p = transform.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * r;

            return NavMesh.SamplePosition(p, out var hit, 1f, NavMesh.AllAreas) ? hit.position : transform.position;
        }

        private void Update()
        {
            if (Time.time < nextAssign) return;
            nextAssign = Time.time + reassignInterval;
            Assign();
        }

        // Closest enemies first; each keeps its slot or takes the free slot nearest its bearing
        private void Assign()
        {
            members.RemoveAll(m => m == null || !m.gameObject.activeInHierarchy);

            int ringCount = Mathf.Max(1, slots);
            int total = ringCount * 3;            // inner ring + overflow ring (twice as many)
            if (taken.Length != total) taken = new bool[total];
            System.Array.Clear(taken, 0, total);

            order.Clear();
            order.AddRange(members);
            Vector3 c = transform.position;
            order.Sort((a, b) => (a.position - c).sqrMagnitude.CompareTo((b.position - c).sqrMagnitude));

            var next = new Dictionary<Transform, int>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                var m = order[i];
                bool inner = i < ringCount;
                int first = inner ? 0 : ringCount;
                int count = inner ? ringCount : ringCount * 2;
                float bearing = Bearing(m.position - c);

                int best = -1;
                float bestDiff = float.MaxValue;
                for (int k = 0; k < count; k++)
                {
                    int s = first + k;
                    if (s >= total || taken[s]) continue;
                    float diff = Mathf.Abs(Mathf.DeltaAngle(bearing, k * (360f / count)));
                    if (diff < bestDiff) { bestDiff = diff; best = s; }
                }

                if (assigned.TryGetValue(m, out int prev) && prev >= first && prev < first + count && !taken[prev])
                {
                    float prevDiff = Mathf.Abs(Mathf.DeltaAngle(bearing, (prev - first) * (360f / count)));
                    if (prevDiff - bestDiff < stickiness) best = prev;
                }

                if (best < 0) continue;
                taken[best] = true;
                next[m] = best;
            }

            assigned.Clear();
            foreach (var kv in next) assigned[kv.Key] = kv.Value;
        }

        private static float Bearing(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            int ringCount = Mathf.Max(1, slots);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            for (int i = 0; i < ringCount; i++)
            {
                float a = i * (360f / ringCount) * Mathf.Deg2Rad;
                Gizmos.DrawWireSphere(transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius, 0.2f);
            }
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.3f);
            for (int i = 0; i < ringCount * 2; i++)
            {
                float a = i * (360f / (ringCount * 2)) * Mathf.Deg2Rad;
                Gizmos.DrawWireSphere(transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * overflowRadius, 0.15f);
            }
        }
    }
}
