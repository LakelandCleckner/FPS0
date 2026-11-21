using UnityEngine;

namespace Combat.Status
{
    // On a particle system inside a status aura: reshapes how its emission follows the stack share.
    public sealed class AuraShareCurve : MonoBehaviour
    {
        [Tooltip("Share of the stack range where this system starts emitting.")]
        [Range(0f, 0.95f)] public float startAt = 0f;
        [Tooltip("Above 1 keeps low shares sparse and ramps up near full.")]
        [Min(0.1f)] public float power = 1f;
        [Tooltip("Emit from this system's own shape and position, not from body points (ground rings).")]
        public bool ownShape = false;

        public bool OwnShape => ownShape;

        public float Evaluate(float share)
        {
            float k = Mathf.Clamp01((share - startAt) / (1f - startAt));
            return Mathf.Pow(k, power);
        }
    }
}
