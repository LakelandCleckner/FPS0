using UnityEngine;

namespace Combat.Effects
{
    // A throwaway runtime "explosion" for when there's no real VFX yet: a translucent
    // sphere at the blast radius that expands slightly and fades out, then destroys
    // itself. Stands in for authored VFX so blasts are VISIBLE during play — a
    // placeholder, not a shippable effect.
    //
    // Spawned by ExplosionOnKillEffect when its blastVfx prefab is null. Builds its
    // own primitive sphere (collider stripped) so it needs no prefab or material asset.
    public class BlastPlaceholderVfx : MonoBehaviour
    {
        private float life;
        private float maxLife;
        private Renderer rend;
        private MaterialPropertyBlock mpb;
        private Color baseColor;
        private float startScale;
        private float endScale;

        public static void Spawn(Vector3 position, float radius, float lifetime, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BlastPlaceholderVFX";

            // No physics — this is purely visual and must not interfere with the blast
            // it represents (or anything else).
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            go.transform.position = position;

            var fx = go.AddComponent<BlastPlaceholderVfx>();
            fx.Setup(radius, lifetime, color);
        }

        private void Setup(float radius, float lifetime, Color color)
        {
            maxLife = Mathf.Max(0.05f, lifetime);
            life = maxLife;
            baseColor = color;

            // A sphere primitive is diameter 1 at scale 1, so scale = 2 * radius to
            // make its RADIUS match the blast. Expand a little over life for a pop.
            startScale = radius * 2f * 0.85f;
            endScale = radius * 2f * 1.05f;
            transform.localScale = Vector3.one * startScale;

            rend = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();

            // URP/Lit and the built-in Standard shader both read _BaseColor/_Color; set
            // both so this shows regardless of pipeline. Transparent-ish look comes from
            // the alpha in the color; on an opaque material it'll read as a solid tint,
            // which is fine for a placeholder.
            ApplyColor(baseColor);
        }

        private void Update()
        {
            life -= Time.deltaTime;
            if (life <= 0f) { Destroy(gameObject); return; }

            float t = 1f - (life / maxLife);   // 0 -> 1 over lifetime

            float s = Mathf.Lerp(startScale, endScale, t);
            transform.localScale = Vector3.one * s;

            var c = baseColor;
            c.a = Mathf.Lerp(baseColor.a, 0f, t);   // fade out
            ApplyColor(c);
        }

        private void ApplyColor(Color c)
        {
            if (rend == null) return;
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);   // URP
            mpb.SetColor("_Color", c);       // built-in
            rend.SetPropertyBlock(mpb);
        }
    }
}
