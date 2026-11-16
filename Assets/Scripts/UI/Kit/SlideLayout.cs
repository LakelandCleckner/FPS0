using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Combat.UI
{
    // Places keyed children by hand and slides them to new spots. New ones fade in, removed ones fade out.
    public class SlideLayout : MonoBehaviour
    {
        [SerializeField] private float slideTime = 0.12f;
        [SerializeField] private float fadeTime = 0.14f;

        private sealed class Entry
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 Target, Velocity;
            public float Alpha;
            public bool Dying;
        }

        private readonly Dictionary<object, Entry> entries = new Dictionary<object, Entry>();
        private readonly HashSet<object> touched = new HashSet<object>();
        private readonly List<object> scratch = new List<object>();

        private static bool Animate => Application.isPlaying;

        public float Width => ((RectTransform)transform).rect.width;

        public void Begin() => touched.Clear();

        // The live child for a key, or null.
        public RectTransform Get(object key) => entries.TryGetValue(key, out var e) && !e.Dying && e.Rect != null ? e.Rect : null;

        // Registers a child at a top-left position in content space; slides there if it already exists.
        public void Place(object key, RectTransform rect, Vector2 position, Vector2 size)
        {
            touched.Add(key);
            if (!entries.TryGetValue(key, out var e) || e.Rect != rect)
            {
                if (e != null && e.Rect != null) UIKit.Destroy(e.Rect.gameObject);
                var group = rect.GetComponent<CanvasGroup>();
                if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
                e = new Entry { Rect = rect, Group = group, Alpha = Animate ? 0f : 1f };
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = position;
                group.alpha = e.Alpha;
                entries[key] = e;
            }
            else if (e.Dying)
            {
                e.Dying = false;
                e.Group.blocksRaycasts = true;
            }

            e.Target = position;
            rect.sizeDelta = size;
            if (!Animate)
            {
                rect.anchoredPosition = position;
                e.Alpha = 1f;
                e.Group.alpha = 1f;
            }
        }

        // Fades out everything not placed since Begin and sets the content height. Red tints a removed child first.
        public void End(float height, object red = null, Color redColor = default)
        {
            scratch.Clear();
            foreach (var kv in entries)
                if (!touched.Contains(kv.Key) && !kv.Value.Dying) scratch.Add(kv.Key);

            foreach (var key in scratch)
            {
                if (!Animate) { Remove(key); continue; }
                var e = entries[key];
                e.Dying = true;
                e.Group.blocksRaycasts = false;
                if (red != null && Equals(key, red) && e.Rect != null)
                {
                    var tint = UIKit.Solid(e.Rect, "Removed", redColor);
                    UIKit.Stretch(tint.rectTransform);
                    UIKit.Ignore(tint);
                }
            }

            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, Mathf.Max(0f, height));
        }

        private void Update()
        {
            if (entries.Count == 0) return;
            float dt = Time.unscaledDeltaTime;
            scratch.Clear();
            foreach (var kv in entries)
            {
                var e = kv.Value;
                if (e.Rect == null) { scratch.Add(kv.Key); continue; }

                if (e.Dying)
                {
                    e.Alpha = Mathf.MoveTowards(e.Alpha, 0f, dt / Mathf.Max(0.01f, fadeTime));
                    e.Group.alpha = e.Alpha;
                    if (e.Alpha <= 0f) scratch.Add(kv.Key);
                    continue;
                }

                if (e.Alpha < 1f)
                {
                    e.Alpha = Mathf.MoveTowards(e.Alpha, 1f, dt / Mathf.Max(0.01f, fadeTime));
                    e.Group.alpha = e.Alpha;
                }

                var p = e.Rect.anchoredPosition;
                if ((p - e.Target).sqrMagnitude > 0.01f)
                    e.Rect.anchoredPosition = Vector2.SmoothDamp(p, e.Target, ref e.Velocity, slideTime, Mathf.Infinity, dt);
                else
                {
                    e.Rect.anchoredPosition = e.Target;
                    e.Velocity = Vector2.zero;
                }
            }
            foreach (var key in scratch) Remove(key);
        }

        private void Remove(object key)
        {
            if (entries.TryGetValue(key, out var e) && e.Rect != null) UIKit.Destroy(e.Rect.gameObject);
            entries.Remove(key);
        }
    }
}
