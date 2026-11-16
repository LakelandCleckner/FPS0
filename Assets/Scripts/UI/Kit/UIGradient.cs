using UnityEngine;
using UnityEngine.UI;

namespace Combat.UI
{
    // Multiplies a linear colour ramp over a graphic's vertices. Exact on sliced images, since their rows are straight.
    [RequireComponent(typeof(Graphic))]
    public class UIGradient : BaseMeshEffect
    {
        [Tooltip("Top, or left when horizontal.")]
        [SerializeField] private Color from = Color.white;
        [Tooltip("Bottom, or right when horizontal.")]
        [SerializeField] private Color to = Color.white;
        [SerializeField] private bool horizontal;

        public void Set(Color from, Color to, bool horizontal)
        {
            this.from = from;
            this.to = to;
            this.horizontal = horizontal;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float p = horizontal ? v.position.x : v.position.y;
                if (p < min) min = p;
                if (p > max) max = p;
            }

            float span = Mathf.Max(0.0001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = ((horizontal ? v.position.x : v.position.y) - min) / span;
                Color ramp = horizontal ? Color.Lerp(from, to, t) : Color.Lerp(to, from, t);
                v.color = (Color)v.color * ramp;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
