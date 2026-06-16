using UnityEngine;
using TMPro;

namespace Combat.Feedback
{
    // One floating damage number. Size, lifetime, and rise speed are all set at
    // spawn (locked, never resizes) so bigger hits are larger, linger longer,
    // and hang near the enemy by rising slower. Font, style (bold/italic), and
    // color gradient come from the damage type's 2x2 (crit x debuff) state.
    // Pure presentation — holds no combat state.
    [RequireComponent(typeof(TextMeshPro))]
    public class DamageNumber : MonoBehaviour
    {
        private TextMeshPro text;
        private DamageNumberPool pool;
        private float lifetime;
        private float age;
        private Vector3 velocity;
        private float baseAlpha = 1f;
        private Camera cam;

        private void Awake()
        {
            text = GetComponent<TextMeshPro>();
        }

        // Called by the pool when handed out. Sets everything for one showing.
        public void Activate(
            DamageNumberPool pool,
            Vector3 worldPos,
            string content,
            VertexGradient gradient,
            TMP_FontAsset font,
            FontStyles style,
            float lifetime,
            float riseSpeed,
            float horizontalDrift,
            float fontSize)
        {
            this.pool = pool;
            this.lifetime = Mathf.Max(0.05f, lifetime);

            age = 0f;
            transform.position = worldPos;

            text.text = content;
            text.fontSize = fontSize;
            if (font != null) text.font = font;
            text.fontStyle = style;

            // color comes from the type's gradient for this crit/debuff state
            text.enableVertexGradient = true;
            text.colorGradient = gradient;
            baseAlpha = 1f;

            // up + a random left/right drift so rapid numbers fan out
            float drift = Random.Range(-horizontalDrift, horizontalDrift);
            velocity = new Vector3(drift, riseSpeed, 0f);

            cam = Camera.main;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                ReturnToPool();
                return;
            }

            // move
            transform.position += velocity * Time.deltaTime;

            // fade out over the back half of life (overall alpha; gradient kept)
            float t = age / lifetime;
            float alpha = t < 0.5f ? 1f : Mathf.InverseLerp(1f, 0.5f, t);
            text.alpha = alpha * baseAlpha;

            // billboard
            if (cam != null)
                transform.forward = cam.transform.forward;
        }

        private void ReturnToPool()
        {
            gameObject.SetActive(false);
            if (pool != null) pool.Return(this);
        }
    }
}