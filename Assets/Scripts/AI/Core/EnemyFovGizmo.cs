using UnityEngine;

namespace Combat.AI
{
    // Scene-view sight cone for an enemy: yellow while unaware, red while it has a target.
    [RequireComponent(typeof(EnemyPerception))]
    public class EnemyFovGizmo : MonoBehaviour
    {
        public int RayCount = 30;
        public Color IdleColor = new Color(1f, 1f, 0f, 0.6f);
        public Color AlertColor = new Color(1f, 0f, 0f, 0.8f);

        private void OnDrawGizmos()
        {
            var p = GetComponent<EnemyPerception>();
            if (p == null) return;

            bool alert = Application.isPlaying && (p.HasTarget || p.HasLastKnown);
            Vector3 origin = transform.position + Vector3.up * p.EyeHeight;
            float range = p.DetectionRange;
            float half = p.FieldOfViewAngle * 0.5f;

            Gizmos.color = alert ? AlertColor : IdleColor;
            Vector3 prev = origin + Quaternion.Euler(0f, -half, 0f) * transform.forward * range;
            Gizmos.DrawLine(origin, prev);
            for (int i = 1; i <= RayCount; i++)
            {
                float a = -half + p.FieldOfViewAngle * i / RayCount;
                Vector3 next = origin + Quaternion.Euler(0f, a, 0f) * transform.forward * range;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            Gizmos.DrawLine(origin, prev);

            Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, p.SenseRadius);

            if (Application.isPlaying && p.Target != null)
            {
                Gizmos.color = p.TargetVisible ? Color.green : Color.red;
                Gizmos.DrawLine(origin, p.Target.position + Vector3.up * p.TargetAimHeight);
            }
            if (Application.isPlaying && p.HasLastKnown)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(p.LastKnownPosition, 0.5f);
            }
        }
    }
}
