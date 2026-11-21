using System.Collections.Generic;
using UnityEngine;

namespace Combat.Status
{
    // Active status overlays drawn over the body: up to 3 layers, strongest first, each a skinned copy of the body.
    public class StatusOverlay : MonoBehaviour
    {
        private const int MaxLayers = 3;
        private const string LayerName = "StatusOverlay";

        private class Slot
        {
            public StatusSO Status;
            public readonly List<SkinnedMeshRenderer> Renderers = new List<SkinnedMeshRenderer>();
        }

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int BaseYId = Shader.PropertyToID("_BaseY");
        private static readonly int HeightId = Shader.PropertyToID("_Height");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private StatusReceiver receiver;
        private readonly List<SkinnedMeshRenderer> body = new List<SkinnedMeshRenderer>();
        private readonly List<StatusSO> active = new List<StatusSO>();
        private readonly Slot[] slots = new Slot[MaxLayers];
        private readonly List<StatusSO> order = new List<StatusSO>(MaxLayers);
        private MaterialPropertyBlock block;

        public void Bind(StatusReceiver r) => receiver = r;

        public void Add(StatusSO s)
        {
            if (s == null || s.overlayMaterial == null || active.Contains(s)) return;
            active.Add(s);
            enabled = true;
        }

        public void Remove(StatusSO s)
        {
            if (!active.Remove(s)) return;
            for (int i = 0; i < MaxLayers; i++)
                if (slots[i] != null && slots[i].Status == s) Show(slots[i], null);
        }

        private void OnDisable()
        {
            active.Clear();
            for (int i = 0; i < MaxLayers; i++) if (slots[i] != null) Show(slots[i], null);
        }

        private void LateUpdate()
        {
            if (active.Count == 0)
            {
                for (int i = 0; i < MaxLayers; i++) if (slots[i] != null && slots[i].Status != null) Show(slots[i], null);
                enabled = false;
                return;
            }

            // Strongest first: highest share of its stack range.
            order.Clear();
            order.AddRange(active);
            order.Sort((a, b) => Strength(b).CompareTo(Strength(a)));
            if (order.Count > MaxLayers) order.RemoveRange(MaxLayers, order.Count - MaxLayers);

            // Free slots whose status dropped out of the top layers, then fill.
            for (int i = 0; i < MaxLayers; i++)
                if (slots[i] != null && slots[i].Status != null && !order.Contains(slots[i].Status)) Show(slots[i], null);
            foreach (var s in order)
            {
                if (Find(s) != null) continue;
                for (int i = 0; i < MaxLayers; i++)
                {
                    slots[i] ??= new Slot();
                    if (slots[i].Status != null) continue;
                    Show(slots[i], s);
                    break;
                }
            }

            Bounds b = BodyBounds();
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < MaxLayers; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.Status == null) continue;
                float k = slot.Status.overlayScalesWithStacks ? Strength(slot.Status) : 1f;
                int rank = order.IndexOf(slot.Status);
                float fade = receiver != null ? receiver.Fade(slot.Status) : 1f;
                foreach (var r in slot.Renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(block);
                    block.SetFloat(IntensityId, k);
                    block.SetFloat(BaseYId, b.min.y);
                    block.SetFloat(HeightId, b.size.y);
                    block.SetFloat(FadeId, fade);
                    r.SetPropertyBlock(block);
                    r.sortingOrder = MaxLayers - rank;
                }
            }
        }

        private float Strength(StatusSO s)
        {
            if (receiver == null) return 1f;
            int n = receiver.Stacks(s);
            return Mathf.Clamp01((float)n / Mathf.Max(1, s.AuraMaxStacks));
        }

        private Slot Find(StatusSO s)
        {
            for (int i = 0; i < MaxLayers; i++) if (slots[i] != null && slots[i].Status == s) return slots[i];
            return null;
        }

        // Puts a status on a slot (or clears it with null), building the slot's body copies once.
        private void Show(Slot slot, StatusSO s)
        {
            slot.Status = s;
            if (s != null && slot.Renderers.Count == 0) Build(slot);
            foreach (var r in slot.Renderers)
            {
                if (r == null) continue;
                if (s != null)
                {
                    var mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++) mats[m] = s.overlayMaterial;
                    r.sharedMaterials = mats;
                }
                r.enabled = s != null;
            }
        }

        private void Build(Slot slot)
        {
            if (body.Count == 0)
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.sharedMesh != null && !r.name.StartsWith(LayerName)) body.Add(r);

            foreach (var src in body)
            {
                var go = new GameObject(LayerName);
                go.transform.SetParent(src.transform.parent, false);
                go.transform.SetLocalPositionAndRotation(src.transform.localPosition, src.transform.localRotation);
                go.transform.localScale = src.transform.localScale;
                go.layer = src.gameObject.layer;
                var r = go.AddComponent<SkinnedMeshRenderer>();
                r.enabled = false;
                r.sharedMesh = src.sharedMesh;
                r.bones = src.bones;
                r.rootBone = src.rootBone;
                r.localBounds = src.localBounds;
                r.quality = src.quality;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterials = new Material[Mathf.Max(1, src.sharedMesh.subMeshCount)];
                slot.Renderers.Add(r);
            }
        }

        private Bounds BodyBounds()
        {
            bool any = false;
            Bounds b = new Bounds(transform.position + Vector3.up, Vector3.one * 2f);
            foreach (var r in body)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
