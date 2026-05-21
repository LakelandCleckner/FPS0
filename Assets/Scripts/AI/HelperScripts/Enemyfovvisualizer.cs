using UnityEngine;
using CrashKonijn.Goap.Runtime;

namespace GOAPGettingStarted.Behaviours
{
    public class EnemyFOVVisualizer : MonoBehaviour
    {
        public int RayCount = 30;

        [Header("Colors")]
        public Color IdleColor = new Color(1f, 1f, 0f, 0.15f);
        public Color AlertColor = new Color(1f, 0f, 0f, 0.25f);

        private bool isAlerted = false;
        private GoapActionProvider actionProvider;
        private AgentBrain brain;

        private void Awake()
        {
            brain = GetComponent<AgentBrain>();
            actionProvider = GetComponent<GoapActionProvider>();
        }

        private void Update()
        {
            if (actionProvider == null) return;
            isAlerted = actionProvider.CurrentPlan?.Goal is Goals.ChaseGoal
                     || actionProvider.CurrentPlan?.Goal is Goals.InvestigateGoal;
        }

        private void OnDrawGizmos()
        {
            // Read values from AgentBrain — single source of truth
            float detectionRange = brain != null ? brain.DetectionRange : 25f;
            float fovAngle = brain != null ? brain.FieldOfViewAngle : 120f;

            var color = isAlerted ? AlertColor : IdleColor;
            var origin = transform.position + Vector3.up * 1.5f;
            var halfAngle = fovAngle * 0.5f;

            var solidColor = color;
            solidColor.a = isAlerted ? 0.2f : 0.1f;
            Gizmos.color = solidColor;

            for (int i = 0; i <= RayCount; i++)
            {
                var angle = -halfAngle + (fovAngle / RayCount) * i;
                var direction = Quaternion.Euler(0f, angle, 0f) * transform.forward;
                var point = origin + direction * detectionRange;

                if (i > 0)
                    Gizmos.DrawLine(origin, point);
            }

            Gizmos.color = isAlerted ? new Color(1f, 0f, 0f, 0.8f) : new Color(1f, 1f, 0f, 0.6f);

            var leftDir = Quaternion.Euler(0f, -halfAngle, 0f) * transform.forward;
            var rightDir = Quaternion.Euler(0f, halfAngle, 0f) * transform.forward;

            Gizmos.DrawRay(origin, leftDir * detectionRange);
            Gizmos.DrawRay(origin, rightDir * detectionRange);

            var arcPrev = origin + leftDir * detectionRange;
            for (int i = 1; i <= RayCount; i++)
            {
                var angle = -halfAngle + (fovAngle / RayCount) * i;
                var dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;
                var arcPoint = origin + dir * detectionRange;
                Gizmos.DrawLine(arcPrev, arcPoint);
                arcPrev = arcPoint;
            }

            Gizmos.color = new Color(1f, 1f, 1f, 0.1f);
            var circlePrev = origin + transform.forward * detectionRange;
            for (int i = 1; i <= 36; i++)
            {
                var angle = i * 10f;
                var dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;
                var circlePoint = origin + dir * detectionRange;
                Gizmos.DrawLine(circlePrev, circlePoint);
                circlePrev = circlePoint;
            }

            if (Application.isPlaying)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                {
                    var toPlayer = playerObj.transform.position - origin;
                    var dist = toPlayer.magnitude;
                    var mask = LayerMask.GetMask("Default", "Player");

                    if (Physics.Raycast(origin, toPlayer.normalized, out var hit, dist, mask))
                        Gizmos.color = hit.transform.CompareTag("Player") ? Color.green : Color.red;
                    else
                        Gizmos.color = Color.yellow;

                    Gizmos.DrawLine(origin, playerObj.transform.position);
                }
            }
        }
    }
}