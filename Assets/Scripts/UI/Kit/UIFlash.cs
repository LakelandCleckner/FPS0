using UnityEngine;
using UnityEngine.UI;

namespace Combat.UI
{
    // A short overlay that fades out and removes itself.
    public class UIFlash : MonoBehaviour
    {
        private Image image;
        private Color start;
        private float time, duration;

        public static void Play(RectTransform target, UIStyleSO style, Color color, float duration = 0.45f)
        {
            if (target == null || style == null || !Application.isPlaying) return;
            var img = UIKit.Box(target, "Flash", style, color);
            UIKit.Stretch(img.rectTransform);
            UIKit.Ignore(img);
            var f = img.gameObject.AddComponent<UIFlash>();
            f.image = img;
            f.start = color;
            f.duration = Mathf.Max(0.05f, duration);
        }

        private void Update()
        {
            time += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(time / duration);
            if (image != null) image.color = new Color(start.r, start.g, start.b, start.a * k * k);
            if (k <= 0f) UIKit.Destroy(gameObject);
        }
    }
}
