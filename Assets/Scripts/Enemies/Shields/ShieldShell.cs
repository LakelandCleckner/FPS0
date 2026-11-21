using UnityEngine;

namespace Combat.Enemies
{
    // Root of a shield shell prefab: its size at scale 1, fixed-size parts and spinning parts. Measured by FPS0/Art/Build Shield VFX.
    public class ShieldShell : MonoBehaviour
    {
        [Tooltip("Shell radius at scale 1 (m).")]
        [Min(0.01f)] public float baseRadius = 1f;
        [Tooltip("Shell centre relative to this root at scale 1.")]
        public Vector3 centerOffset;
        [Tooltip("Orbit shell: scaled evenly, and only the part orbit stretches to the body, so parts never warp.")]
        public bool uniformFit;

        [Header("Fixed-size parts")]
        [Tooltip("Parts that keep their size while the shell stretches to the body (rocks, crystals).")]
        public Transform[] fixedParts = new Transform[0];
        [Tooltip("Size of the fixed parts relative to the prefab.")]
        [Min(0.01f)] public float partSize = 1f;

        [Tooltip("Rim-glow bubble; stretches with the orbit.")]
        public Renderer rim;
        [Tooltip("Rim size; 1 = the fitted body size plus padding.")]
        [Range(0.5f, 1.5f)] public float rimSize = 1f;
        [Tooltip("Part orbit relative to the fitted size; above 1 keeps the parts outside the rim.")]
        [Range(0.5f, 2f)] public float orbitSize = 1.25f;

        [Header("Spin")]
        public Transform outer;
        public float outerSpeed = 20f;
        public Transform inner;
        public float innerSpeed = -10f;

        [HideInInspector] public int setupVersion;

        // Vertical orbit stretch for uniform shells, set by ShieldVisual.
        [System.NonSerialized] public float stretchY = 1f;

        private Vector3[] baseScales;
        private Vector3[] basePos;
        private Quaternion[] baseRot;
        private float[] speeds;
        private float outerAngle, innerAngle;
        private float bandFix = 1f;
        private Vector3 rimBase;
        private MaterialPropertyBlock block;
        private static readonly int FlashId = Shader.PropertyToID("_Flash");

        // Orbiting parts move under the root so the orbit can stretch without warping them.
        private void Awake()
        {
            int n = fixedParts.Length;
            baseScales = new Vector3[n];
            basePos = new Vector3[n];
            baseRot = new Quaternion[n];
            speeds = new float[n];
            for (int i = 0; i < n; i++)
            {
                var p = fixedParts[i];
                if (p == null) continue;
                speeds[i] = outer != null && p.IsChildOf(outer) ? 1f : inner != null && p.IsChildOf(inner) ? -1f : 0f;
                if (speeds[i] != 0f) p.SetParent(transform, true);
                basePos[i] = p.localPosition;
                baseRot[i] = p.localRotation;
                baseScales[i] = p.localScale;
            }

            // The parts sit in a flattened band; this makes their orbit round before the body stretch.
            float h = 0f, v = 0f;
            for (int i = 0; i < n; i++)
            {
                if (fixedParts[i] == null || speeds[i] == 0f) continue;
                var d = basePos[i] - centerOffset;
                h = Mathf.Max(h, new Vector2(d.x, d.z).magnitude);
                v = Mathf.Max(v, Mathf.Abs(d.y));
            }
            bandFix = v > 0.01f ? h / v : 1f;
            if (rim != null) rimBase = rim.transform.localScale;
        }

        // Brightens the rim, 0 to 1. Keeps any tint already on the renderer.
        public void SetFlash(float f)
        {
            if (rim == null) return;
            block ??= new MaterialPropertyBlock();
            rim.GetPropertyBlock(block, 0);
            block.SetFloat(FlashId, f);
            rim.SetPropertyBlock(block, 0);
        }

        // Undoes the shell's own scale on the fixed parts.
        public void KeepPartSize(Vector3 shellScale)
        {
            if (baseScales == null) return;
            float s = Mathf.Max(0.01f, Mathf.Max(shellScale.x, shellScale.y, shellScale.z));
            float k = partSize / s;
            for (int i = 0; i < fixedParts.Length; i++)
                if (fixedParts[i] != null) fixedParts[i].localScale = baseScales[i] * k;
        }

        // Point on the shell (local space) in a direction, following the orbit stretch.
        public Vector3 Surface(Vector3 dir)
        {
            var p = dir.normalized * baseRadius;
            if (uniformFit) p.y *= stretchY;
            return p + centerOffset;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            outerAngle += outerSpeed * dt;
            innerAngle += innerSpeed * dt;
            if (basePos == null) return;
            if (rim != null) rim.transform.localScale = new Vector3(rimBase.x, rimBase.y * stretchY, rimBase.z) * rimSize;
            var qo = Quaternion.Euler(0f, outerAngle, 0f);
            var qi = Quaternion.Euler(0f, innerAngle, 0f);
            for (int i = 0; i < fixedParts.Length; i++)
            {
                var part = fixedParts[i];
                if (part == null || speeds[i] == 0f) continue;
                var q = speeds[i] > 0f ? qo : qi;
                var p = q * (basePos[i] - centerOffset);
                p.y *= stretchY * bandFix;
                part.localPosition = centerOffset + p * orbitSize;
                part.localRotation = q * baseRot[i];
            }
        }
    }
}
