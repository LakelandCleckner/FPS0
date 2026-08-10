#if UNITY_EDITOR
using UnityEngine;
using Combat.Effects;

namespace Combat.Testing
{
    // Editor-only radius visualiser for authoring blast VFX. Drop it on the same
    // object as your VFX (or an empty at the blast origin), point it at the
    // ExplosionOnKillSO asset, and it draws that asset's radius as a wire sphere so
    // you can size and align the effect to match the real hit area.
    //
    // It reads the SO's radius live, so tweaking the asset updates the gizmo without
    // touching this. Purely a scene aid — compiled out of builds.
    [ExecuteAlways]
    public class BlastRadiusGizmo : MonoBehaviour
    {
        [Tooltip("The explosion asset whose radius to draw. Leave the manual radius at " +
                 "0 to use this; set a manual radius to override for a quick preview.")]
        public ExplosionOnKillSO explosion;

        [Tooltip("Manual radius override. 0 = read from the explosion asset above.")]
        public float manualRadius = 0f;

        [Tooltip("Also draw a solid translucent sphere, not just the wire outline.")]
        public bool solid = true;

        public Color color = new Color(1f, 0.4f, 0.1f, 1f);

        private float Radius =>
            manualRadius > 0f ? manualRadius
            : (explosion != null ? explosion.radius : 0f);

        private void OnDrawGizmos()
        {
            float r = Radius;
            if (r <= 0f) return;

            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, r);

            if (solid)
            {
                var c = color; c.a = 0.12f;
                Gizmos.color = c;
                Gizmos.DrawSphere(transform.position, r);
            }
        }
    }
}
#endif
