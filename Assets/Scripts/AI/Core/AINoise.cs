using System;
using UnityEngine;

namespace Combat.AI
{
    // Sounds enemies can hear (gunfire for now). Each listener checks the distance against its own hearing radius.
    public static class AINoise
    {
        // position, the transform that made it (the player, for gunfire)
        public static event Action<Vector3, Transform> Heard;

        public static void Emit(Vector3 position, Transform source) => Heard?.Invoke(position, source);
    }
}
