using UnityEngine;
using Combat.Spawning;

namespace GOAPGettingStarted.Behaviours
{
    public class EnemyMemory : MonoBehaviour, IPoolable
    {
        public Vector3 LastKnownPlayerPosition { get; set; }
        public bool HasLastKnownPosition { get; set; }

        // Flat direction the player was moving when last seen; the search looks that way
        public Vector3 LastKnownHeading { get; set; }

        // Reused enemy: forget everything. Without this a respawned enemy remembers
        // where it last saw the player and can immediately go investigate a position
        // from its previous life. Two fields, but they must clear.
        public void OnSpawn()
        {
            HasLastKnownPosition = false;
            LastKnownPlayerPosition = default;
            LastKnownHeading = default;
        }

        public void OnDespawn() { }
    }
}