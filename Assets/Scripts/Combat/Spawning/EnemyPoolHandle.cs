using UnityEngine;

namespace Combat.Spawning
{
    // Wraps a pooled enemy instance and caches its IPoolable components once, so a
    // spawn/return doesn't GetComponents every time. Also the natural place to hang a
    // back-reference from the enemy to "which pool do I return to" later.
    public class EnemyPoolHandle
    {
        public GameObject GameObject { get; }
        private readonly IPoolable[] poolables;

        public EnemyPoolHandle(GameObject go)
        {
            GameObject = go;
            // includeInactive: true so components on disabled children (and the
            // inactive root itself) are still found — the instance is created inactive.
            poolables = go.GetComponentsInChildren<IPoolable>(true);
        }

        public void InvokeSpawn()
        {
            for (int i = 0; i < poolables.Length; i++)
                poolables[i].OnSpawn();
        }

        public void InvokeDespawn()
        {
            for (int i = 0; i < poolables.Length; i++)
                poolables[i].OnDespawn();
        }
    }
}