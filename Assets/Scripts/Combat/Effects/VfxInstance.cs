using UnityEngine;

namespace Combat.Effects
{
    // Per-instance bookkeeping for VfxPool.
    public sealed class VfxInstance : MonoBehaviour
    {
        internal GameObject Prefab;
        internal Vector3 BaseScale;
        internal Color Tint;
        internal bool Colorize;
        internal float ReleaseAt;
        internal bool Timed;
        private ParticleSystem[] systems;
        private TrailRenderer[] trails;

        // Replays particles and clears trails so a reused instance starts fresh.
        internal void Restart()
        {
            systems ??= GetComponentsInChildren<ParticleSystem>(true);
            trails ??= GetComponentsInChildren<TrailRenderer>(true);
            for (int i = 0; i < trails.Length; i++)
                if (trails[i] != null) trails[i].Clear();
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                systems[i].Clear(false);
                systems[i].Play(false);
            }
        }
    }
}
