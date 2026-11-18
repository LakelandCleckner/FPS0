using UnityEngine;

namespace Combat.Effects
{
    // Drives timed releases for VfxPool.
    public sealed class VfxPoolRunner : MonoBehaviour
    {
        private void Update() => VfxPool.Tick();
    }
}
