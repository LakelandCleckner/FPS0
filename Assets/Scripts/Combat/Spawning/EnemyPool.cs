using System.Collections.Generic;
using UnityEngine;

namespace Combat.Spawning
{
    // One pool for one enemy prefab. Same-type enemies are interchangeable, so each
    // TYPE gets its own pool; the spawner owns a pool per registered prefab.
    // The enemy binds its own GOAP agent type in Awake (EnemyBrain), so no setup order is needed here.
    public class EnemyPool
    {
        private readonly GameObject prefab;
        private readonly Transform parent;
        private readonly System.Action<GameObject> setup;
        private readonly Stack<EnemyPoolHandle> free = new Stack<EnemyPoolHandle>();

        public GameObject Prefab => prefab;

        // setup runs once per new instance, before its first Start (e.g. assigning its enemy variant).
        public EnemyPool(GameObject prefab, Transform parent, System.Action<GameObject> setup = null)
        {
            this.prefab = prefab;
            this.parent = parent;
            this.setup = setup;
        }

        // Creates instances up front and parks them in the pool, so first spawns don't hitch.
        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
                Return(Create(parent.position, parent.rotation));
        }

        public EnemyPoolHandle Rent(Vector3 position, Quaternion rotation)
        {
            EnemyPoolHandle handle;

            if (free.Count > 0)
            {
                // Reused instance: deactivated on Return. Move, reactivate, reset.
                handle = free.Pop();
                var g = handle.GameObject;
                g.transform.SetPositionAndRotation(position, rotation);
                g.SetActive(true);
                handle.InvokeSpawn();
            }
            else
            {
                // First creation: components initialise via their own Awake/Start;
                // OnSpawn is the reset path for reuse only.
                handle = Create(position, rotation);
            }

            return handle;
        }

        public void Return(EnemyPoolHandle handle)
        {
            if (handle == null) return;

            // Reset/stop BEFORE deactivating, so despawn logic can still touch live
            // components. This ordering is what status-transfer-on-death depends on.
            handle.InvokeDespawn();

            var go = handle.GameObject;
            go.SetActive(false);
            go.transform.SetParent(parent, worldPositionStays: false);

            free.Push(handle);
        }

        // setup runs before the instance's first Start (e.g. assigning its enemy variant).
        private EnemyPoolHandle Create(Vector3 position, Quaternion rotation)
        {
            var go = Object.Instantiate(prefab, position, rotation, parent);
            setup?.Invoke(go);
            return new EnemyPoolHandle(go);
        }
    }
}