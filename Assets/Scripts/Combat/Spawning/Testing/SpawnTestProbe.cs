#if UNITY_EDITOR || STATUS_DEBUG
using UnityEngine;
using Combat.Spawning;

namespace Combat.Testing
{
    // THROWAWAY spawn tester. Debug SpawnOne (comma) spawns one "basic" enemy, SpawnGroup (period) spawns three.
    // Spawns in front of this object (or the camera) so they land in view. Delete once
    // the real spawning flow (a spawner, encounter triggers) exists.
    public class SpawnTestProbe : MonoBehaviour
    {
        [SerializeField] private EnemySpawner spawner;

        [Tooltip("Type id to spawn. Matches the id registered on the EnemySpawner.")]
        [SerializeField] private string enemyId = "basic";

        [Header("Placement")]
        [Tooltip("How far in front of this object to spawn.")]
        [SerializeField] private float spawnDistance = 6f;
        [Tooltip("Scatter radius for the group (period key).")]
        [SerializeField] private float groupRadius = 3f;
        [SerializeField] private int groupCount = 3;

        private PlayerInputRouter input;

        private void Update()
        {
            if (input == null) input = PlayerInputRouter.For(this);
            if (input == null) return;

            if (input.Debug.SpawnOne.WasPressedThisFrame())
            {
                Vector3 pos = SpawnPoint();
                var h = spawner.Spawn(enemyId, pos);
                Debug.Log(h != null
                    ? $"[SpawnTest] spawned one '{enemyId}' at {pos}"
                    : $"[SpawnTest] FAILED to spawn '{enemyId}' - id registered on the spawner?");
            }

            if (input.Debug.SpawnGroup.WasPressedThisFrame())
            {
                Vector3 center = SpawnPoint();
                var list = spawner.SpawnGroup(enemyId, groupCount, center, groupRadius);
                Debug.Log($"[SpawnTest] spawned {list.Count}/{groupCount} '{enemyId}' around {center}");
            }
        }

        // A point in front of this object on the ground plane. Uses the object's
        // forward; if this is on the player/camera, enemies appear ahead of you.
        private Vector3 SpawnPoint()
        {
            Vector3 p = transform.position + transform.forward * spawnDistance;
            p.y = transform.position.y;   // keep on the caster's height; NavMesh will settle it
            return p;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 c = transform.position + transform.forward * spawnDistance;
            c.y = transform.position.y;
            Gizmos.DrawWireSphere(c, groupRadius);
        }
    }
}
#endif
