using System.Collections.Generic;
using UnityEngine;

namespace Combat.Spawning
{
    // Owns a pool per enemy type and exposes spawning. Two layers:
    //   Spawn      — the PRIMITIVE: one enemy of a type at an exact position. Every
    //                other spawn form is built on this.
    //   SpawnGroup — X of one type scattered around a point, the form you'll usually
    //                call. Loops the primitive with a placement offset so they don't
    //                stack.
    // A future squad layer loops SpawnGroup/Spawn per member; because Spawn returns the
    // instance, a squad can track membership.
    public class EnemySpawner : MonoBehaviour
    {
        [System.Serializable]
        public struct EnemyType
        {
            [Tooltip("Identifier used to spawn this type (e.g. \"Grunt\").")]
            public string id;
            public GameObject prefab;
        }

        [Header("Registered enemy types")]
        [SerializeField] private EnemyType[] types;

        [Tooltip("Inactive pooled instances are parented here. Leave empty to use this " +
                 "spawner's own transform.")]
        [SerializeField] private Transform poolParent;

        private readonly Dictionary<string, EnemyPool> poolsById
            = new Dictionary<string, EnemyPool>();

        private void Awake()
        {
            var parent = poolParent != null ? poolParent : transform;
            if (types != null)
            {
                foreach (var t in types)
                {
                    if (t.prefab == null || string.IsNullOrEmpty(t.id)) continue;
                    if (poolsById.ContainsKey(t.id))
                    {
                        Debug.LogWarning($"[EnemySpawner] Duplicate type id '{t.id}', ignoring.");
                        continue;
                    }
                    poolsById[t.id] = new EnemyPool(t.prefab, parent);
                }
            }
        }

        // THE PRIMITIVE — one enemy of a type at a position. Returns the handle so a
        // caller (a squad, an encounter) can track what it spawned. Null if the type
        // id isn't registered.
        public EnemyPoolHandle Spawn(string typeId, Vector3 position, Quaternion rotation)
        {
            if (!poolsById.TryGetValue(typeId, out var pool))
            {
                Debug.LogError($"[EnemySpawner] No enemy type registered for id '{typeId}'.");
                return null;
            }

            var handle = pool.Rent(position, rotation);

            // Tell the instance how to get home, so its death handler can return it to
            // THIS pool with THIS type id. Bound every spawn (the handle is reused, but
            // the binding is cheap and keeps a reused instance correct even if it were
            // ever moved between pools).
            var pooled = handle.GameObject.GetComponent<PooledEnemy>();
            if (pooled != null)
                pooled.Bind(this, typeId, handle);

            return handle;
        }

        public EnemyPoolHandle Spawn(string typeId, Vector3 position)
            => Spawn(typeId, position, Quaternion.identity);

        // X of ONE type scattered in a radius around a center, so they don't spawn
        // inside each other. Random-within-radius placement — the simplest strategy;
        // authored spawn points or formations can layer on later without changing the
        // primitive. Returns the spawned handles for tracking.
        public List<EnemyPoolHandle> SpawnGroup(
            string typeId, int count, Vector3 center, float radius)
        {
            var spawned = new List<EnemyPoolHandle>(count);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = center + ScatterOffset(radius);
                var h = Spawn(typeId, pos);
                if (h != null) spawned.Add(h);
            }
            return spawned;
        }

        // Return a spawned enemy to its pool. The caller (or the enemy's own death
        // handler) invokes this AFTER any on-death reactions have run — the pool's
        // Return then resets and deactivates, in that order.
        public void Despawn(string typeId, EnemyPoolHandle handle)
        {
            if (handle == null) return;
            if (poolsById.TryGetValue(typeId, out var pool))
                pool.Return(handle);
            else
                Debug.LogError($"[EnemySpawner] Can't despawn — no pool for id '{typeId}'.");
        }

        // Random horizontal offset within the radius, on the same Y as the center so
        // enemies don't spawn in the air or in the floor. NavMesh sampling would make
        // this robust on uneven ground — a later refinement.
        private static Vector3 ScatterOffset(float radius)
        {
            if (radius <= 0f) return Vector3.zero;
            Vector2 c = Random.insideUnitCircle * radius;
            return new Vector3(c.x, 0f, c.y);
        }
    }
}