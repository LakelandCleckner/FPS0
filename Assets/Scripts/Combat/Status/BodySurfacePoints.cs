using UnityEngine;

namespace Combat.Status
{
    // Skin points baked from the body mesh (FPS0/Art/Bake Body Surface Points), used by Skin auras.
    [DisallowMultipleComponent]
    public sealed class BodySurfacePoints : MonoBehaviour
    {
        [SerializeField] private StatusAura.BodyPoint[] points = System.Array.Empty<StatusAura.BodyPoint>();
        [SerializeField] private SkinnedMeshRenderer source;

        public StatusAura.BodyPoint[] Points => points;
        public SkinnedMeshRenderer Source => source;

        public void Set(StatusAura.BodyPoint[] baked, SkinnedMeshRenderer from)
        {
            points = baked;
            source = from;
        }
    }
}
