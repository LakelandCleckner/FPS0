using Unity.AI.Navigation;
using UnityEngine;

namespace Combat.Encounters
{
    // A generated room: remembers how it was made and rebuilds the NavMesh on start (from colliders) so its walls and cover count.
    public class EncounterRoom : MonoBehaviour
    {
        public EncounterRoomSettingsSO settings;
        public int seed;

        private static int builtFrame = -1;

        private void Start() => RebuildNavMesh(gameObject);

        // Rebuild every NavMesh surface in the scene; with none, give the room its own
        public static void RebuildNavMesh(GameObject room)
        {
            // Several rooms starting together share one rebuild
            if (builtFrame == Time.frameCount) return;
            builtFrame = Time.frameCount;

            if (NavMeshSurface.activeSurfaces.Count == 0)
            {
                var own = room.GetComponent<NavMeshSurface>();
                if (own == null)
                {
                    own = room.AddComponent<NavMeshSurface>();
                    own.collectObjects = CollectObjects.Children;
                }
                Build(own);
                return;
            }
            foreach (var s in NavMeshSurface.activeSurfaces.ToArray()) Build(s);
        }

        // From colliders (no mesh read access needed, matches what blocks movement), never the player
        private static void Build(NavMeshSurface s)
        {
            s.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            int player = LayerMask.NameToLayer("Player");
            if (player >= 0) s.layerMask &= ~(1 << player);
            s.BuildNavMesh();
        }
    }
}
