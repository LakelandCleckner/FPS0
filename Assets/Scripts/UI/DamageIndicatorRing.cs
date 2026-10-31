using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Combat.Core;

namespace Combat.UI
{
    // Ring of arcs around the crosshair pointing at whoever hit the player; one arc per attacker, tracks them while it fades.
    [RequireComponent(typeof(RectTransform))]
    public class DamageIndicatorRing : MonoBehaviour
    {
        [SerializeField] private PlayerHurtbox hurtbox;
        [Tooltip("Camera whose facing decides arc direction. Defaults to Camera.main.")]
        [SerializeField] private Transform viewTransform;

        [Header("Look")]
        [SerializeField] private float diameter = 260f;
        [Range(0.02f, 0.5f)][SerializeField] private float thickness = 0.08f;
        [Range(5f, 180f)][SerializeField] private float arcDegrees = 50f;
        [SerializeField] private Color color = new Color(0.9f, 0.12f, 0.1f, 0.9f);

        [Header("Timing")]
        [SerializeField] private float holdTime = 0.4f;
        [SerializeField] private float fadeTime = 0.8f;
        [Tooltip("Damage that gives a full-strength arc; smaller hits start fainter.")]
        [SerializeField] private float fullAlphaDamage = 40f;
        [Range(0f, 1f)][SerializeField] private float minAlpha = 0.45f;

        private sealed class Arc
        {
            public Image Image;
            public Transform Attacker;
            public Vector3 LastPos;
            public float Time;
            public float Strength;
        }

        private readonly List<Arc> active = new List<Arc>();
        private readonly Stack<Arc> pool = new Stack<Arc>();
        private Sprite ringSprite;

        private void Awake()
        {
            ringSprite = BuildRingSprite(256, thickness);
            if (hurtbox == null) hurtbox = FindFirstObjectByType<PlayerHurtbox>();
        }

        private void OnEnable()
        {
            if (hurtbox != null) hurtbox.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (hurtbox != null) hurtbox.Damaged -= OnDamaged;
        }

        private void OnDamaged(Transform attacker, Vector3 from, float amount)
        {
            Arc arc = null;
            if (attacker != null)
                for (int i = 0; i < active.Count; i++)
                    if (active[i].Attacker == attacker) { arc = active[i]; break; }

            if (arc == null)
            {
                arc = pool.Count > 0 ? pool.Pop() : CreateArc();
                arc.Image.gameObject.SetActive(true);
                arc.Attacker = attacker;
                active.Add(arc);
            }

            arc.LastPos = from;
            arc.Time = Time.time;
            arc.Strength = Mathf.Lerp(minAlpha, 1f, fullAlphaDamage > 0f ? Mathf.Clamp01(amount / fullAlphaDamage) : 1f);
            Place(arc);
        }

        private void Update()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var arc = active[i];
                float age = Time.time - arc.Time;
                if (age >= holdTime + fadeTime)
                {
                    arc.Image.gameObject.SetActive(false);
                    arc.Attacker = null;
                    active.RemoveAt(i);
                    pool.Push(arc);
                    continue;
                }

                if (arc.Attacker != null && arc.Attacker.gameObject.activeInHierarchy)
                    arc.LastPos = arc.Attacker.position;

                Place(arc);
                float fade = age <= holdTime ? 1f : 1f - (age - holdTime) / Mathf.Max(0.01f, fadeTime);
                var c = color;
                c.a *= arc.Strength * fade;
                arc.Image.color = c;
            }
        }

        // Arc centred on the attacker's bearing: 0 = ahead (top), 90 = right.
        private void Place(Arc arc)
        {
            Transform view = viewTransform != null ? viewTransform : (Camera.main != null ? Camera.main.transform : null);
            if (view == null) return;

            Vector3 fwd = view.forward; fwd.y = 0f;
            Vector3 to = arc.LastPos - view.position; to.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f) return;

            float bearing = Vector3.SignedAngle(fwd, to, Vector3.up);
            arc.Image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, arcDegrees * 0.5f - bearing);
            arc.Image.fillAmount = arcDegrees / 360f;
        }

        private Arc CreateArc()
        {
            var go = new GameObject("DamageArc", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);

            var img = go.GetComponent<Image>();
            img.sprite = ringSprite;
            img.raycastTarget = false;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.color = color;

            return new Arc { Image = img };
        }

        // Soft-edged white ring; thickness is a fraction of the radius.
        private static Sprite BuildRingSprite(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float half = size * 0.5f;
            float outer = half - 1f;
            float inner = outer - Mathf.Max(2f, half * thickness);
            float mid = (outer + inner) * 0.5f;
            float halfWidth = (outer - inner) * 0.5f;
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - mid);
                    float a = Mathf.Clamp01(halfWidth - d + 0.5f);
                    float glow = Mathf.Clamp01(1f - d / halfWidth) * 0.35f;
                    byte b = (byte)(Mathf.Clamp01(Mathf.Max(a, glow)) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, b);
                }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
