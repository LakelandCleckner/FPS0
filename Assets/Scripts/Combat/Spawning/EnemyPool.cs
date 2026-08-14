using System.Collections.Generic;
using UnityEngine;

namespace Combat.Spawning
{
    // One pool for one enemy prefab. Same-type enemies are interchangeable, so each
    // TYPE gets its own pool; the spawner owns a pool per registered prefab.
    //
    // GOAP INSTANTIATION ORDER matters here. A prefab asset can't serialize a
    // reference to the scene's GoapBehaviour, so an AgentTypeBehaviour on a clone comes
    // up with a null runner and NREs in Awake. The fix (see GoapRuntimeInit): remove
    // AgentTypeBehaviour from the prefab and assign the AgentType in code — but the
    // provider's Awake still throws in Start if no type is set. So we instantiate
    // INACTIVE (no Awake yet), assign the type while inactive via GoapRuntimeInit, THEN
    // activate. The gap between instantiate and activate is where the type gets set.
    public class EnemyPool
    {
        private readonly GameObject prefab;
        private readonly Transform parent;
        private readonly Stack<EnemyPoolHandle> free = new Stack<EnemyPoolHandle>();

        public GameObject Prefab => prefab;

        public EnemyPool(GameObject prefab, Transform parent)
        {
            this.prefab = prefab;
            this.parent = parent;
        }

        public EnemyPoolHandle Rent(Vector3 position, Quaternion rotation)
        {
            EnemyPoolHandle handle;

            if (free.Count > 0)
            {
                // Reused instance: already initialised (GOAP type assigned) on its
                // first creation, then deactivated on Return. Move, reactivate, reset.
                handle = free.Pop();
                var g = handle.GameObject;
                g.transform.SetPositionAndRotation(position, rotation);
                g.SetActive(true);
                handle.InvokeSpawn();
            }
            else
            {
                // First creation instantiates active and assigns the GOAP type the
                // same frame, before the provider's Start runs. No InvokeSpawn here —
                // components initialise via their own Awake/Start; OnSpawn is the RESET
                // path for REUSE only.
                handle = CreateInactiveAndInit(position, rotation);
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

        // Instantiate and assign the GOAP AgentType in the SAME frame. The provider's
        // Awake is null-safe (it guards `if (AgentTypeBehaviour != null)`), and the
        // only thing that throws on a missing type is the provider's Start — which
        // runs the frame AFTER Instantiate. Assigning the type now (same frame as
        // Instantiate) beats that Start. Requires AgentTypeBehaviour REMOVED from the
        // prefab so its own Awake can't NRE on the null runner.
        private EnemyPoolHandle CreateInactiveAndInit(Vector3 position, Quaternion rotation)
        {
            var go = Object.Instantiate(prefab, position, rotation, parent);

            var init = go.GetComponent<GoapRuntimeInit>();
            if (init != null)
                init.Initialize();
            else
                Debug.LogWarning("[EnemyPool] Enemy prefab has no GoapRuntimeInit — " +
                                 "its AgentType won't be assigned and GOAP will throw.");

            return new EnemyPoolHandle(go);
        }
    }
}