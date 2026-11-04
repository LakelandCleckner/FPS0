using System.Collections.Generic;
using UnityEngine;

namespace Combat.Enemies
{
    // Sits on the player. Tuning for how enemies close in: they rush straight at the player and stop at
    // contact range; NavMesh avoidance spills late arrivals around the sides. An enemy held up (blocked by
    // another enemy, or stuck short of the player) heads for the nearest open gap around the player.
    public class SurroundRing : MonoBehaviour
    {
        [Tooltip("Enemies stop this far from the player's centre (m).")]
        [SerializeField] private float approachDistance = 1.2f;
        [Tooltip("Within this distance enemies turn to face the player instead of their path (m).")]
        [SerializeField] private float faceDistance = 3f;

        [Header("Flanking when blocked")]
        [Tooltip("Seconds stuck short of the player (crowded out) before swinging round the side.")]
        [SerializeField] private float flankAfter = 0.75f;
        [Tooltip("Fallback when no gap is found: how far round toward the player's back to go (degrees).")]
        [SerializeField] private float flankAngle = 70f;
        [Tooltip("Smallest open arc around the player worth moving into (degrees).")]
        [SerializeField] private float minGap = 50f;
        [Tooltip("Seconds an enemy heads for its flank point before rushing straight again.")]
        [SerializeField] private float flankHold = 1.5f;
        [Tooltip("Below this speed an enemy short of the player counts as held up (m/s).")]
        [SerializeField] private float blockedSpeed = 0.3f;
        [Tooltip("Only enemies within this distance try to flank (m).")]
        [SerializeField] private float flankRange = 4f;

        [Header("Leading")]
        [Tooltip("Chasing enemies aim this far ahead of the player's movement, at most (s).")]
        [SerializeField] private float maxLeadTime = 0.5f;

        public float ApproachDistance => approachDistance;
        public float FaceDistance => faceDistance;
        public float FlankAfter => flankAfter;
        public float FlankAngle => flankAngle;
        public float FlankHold => flankHold;
        public float BlockedSpeed => blockedSpeed;
        public float FlankRange => flankRange;
        public float MaxLeadTime => maxLeadTime;

        // Player's flat velocity, smoothed
        public Vector3 Velocity { get; private set; }
        private Vector3 lastPosition;

        private void OnEnable() => lastPosition = transform.position;

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 v = (transform.position - lastPosition) / dt;
            v.y = 0f;
            Velocity = Vector3.Lerp(Velocity, v, 1f - Mathf.Exp(-10f * dt));
            lastPosition = transform.position;
        }

        // Enemies currently close to the player (within face distance), for gap finding
        private readonly List<Transform> close = new List<Transform>();
        private readonly List<float> bearings = new List<float>();

        public void SetClose(Transform enemy, bool isClose)
        {
            if (enemy == null) return;
            bool has = close.Contains(enemy);
            if (isClose && !has) close.Add(enemy);
            else if (!isClose && has) close.Remove(enemy);
        }

        // Centre bearing of the open gap nearest this enemy's bearing; false when nothing is wide enough
        public bool FindGap(Transform self, out float gapBearing)
        {
            gapBearing = 0f;
            close.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);

            bearings.Clear();
            Vector3 c = transform.position;
            foreach (var t in close)
                if (t != self) bearings.Add(Bearing(t.position - c));

            float mine = Bearing(self.position - c);
            if (bearings.Count == 0) { gapBearing = mine; return true; }

            bearings.Sort();
            float bestDiff = float.MaxValue;
            bool found = false;
            for (int i = 0; i < bearings.Count; i++)
            {
                float a = bearings[i];
                float b = i + 1 < bearings.Count ? bearings[i + 1] : bearings[0] + 360f;
                if (b - a < minGap) continue;

                float centre = a + (b - a) * 0.5f;
                float diff = Mathf.Abs(Mathf.DeltaAngle(mine, centre));
                if (diff < bestDiff) { bestDiff = diff; gapBearing = centre; found = true; }
            }
            return found;
        }

        private static float Bearing(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.7f);
            DrawCircle(transform.position, approachDistance);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.25f);
            DrawCircle(transform.position, faceDistance);
        }

        private static void DrawCircle(Vector3 c, float r)
        {
            const int segments = 48;
            Vector3 prev = c + new Vector3(0f, 0f, r);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
