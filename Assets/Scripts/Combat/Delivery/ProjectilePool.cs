using System.Collections.Generic;
using UnityEngine;

namespace Combat.Delivery
{
    // Pools projectiles by PREFAB. Different weapons spawn different projectile
    // prefabs, so a single queue would hand a rocket back to a pistol — hence the
    // dictionary rather than the flat Queue that DamageNumberPool uses (it only ever
    // has one prefab).
    //
    // Auto-creates on first use. ProjectileDelivery is a plain class built from a
    // DeliverySO with no scene presence, so requiring a manually-placed pool object
    // would mean every scene that fires a projectile needs setup it can silently
    // forget — and the failure would be a null reference mid-combat.
    public class ProjectilePool : MonoBehaviour
    {
        private static ProjectilePool instance;

        public static ProjectilePool Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindAnyObjectByType<ProjectilePool>();
                if (instance != null) return instance;

                var go = new GameObject("[ProjectilePool]");
                instance = go.AddComponent<ProjectilePool>();
                return instance;
            }
        }

        // Keyed by prefab reference. Prefabs are assets, so the reference is stable
        // and hashes fine.
        private readonly Dictionary<Projectile, Queue<Projectile>> pools
            = new Dictionary<Projectile, Queue<Projectile>>();

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public Projectile Get(Projectile prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            if (!pools.TryGetValue(prefab, out var queue))
            {
                queue = new Queue<Projectile>();
                pools[prefab] = queue;
            }

            Projectile p = null;
            // Loop rather than a single dequeue: a pooled projectile can be destroyed
            // out from under us by a scene unload, leaving a Unity-null in the queue.
            while (queue.Count > 0 && p == null)
                p = queue.Dequeue();

            if (p == null)
            {
                p = Instantiate(prefab, position, rotation, transform);
                p.SetPoolOrigin(prefab);
            }
            else
            {
                p.transform.SetPositionAndRotation(position, rotation);
            }

            p.gameObject.SetActive(true);
            return p;
        }

        public void Return(Projectile projectile, Projectile prefab)
        {
            if (projectile == null) return;

            projectile.gameObject.SetActive(false);
            projectile.transform.SetParent(transform, false);

            if (prefab == null) { Destroy(projectile.gameObject); return; }

            if (!pools.TryGetValue(prefab, out var queue))
            {
                queue = new Queue<Projectile>();
                pools[prefab] = queue;
            }
            queue.Enqueue(projectile);
        }

        // Optional warm-up so the first burst of fire doesn't instantiate mid-combat.
        public void Prewarm(Projectile prefab, int count)
        {
            if (prefab == null || count <= 0) return;

            if (!pools.TryGetValue(prefab, out var queue))
            {
                queue = new Queue<Projectile>();
                pools[prefab] = queue;
            }

            for (int i = 0; i < count; i++)
            {
                var p = Instantiate(prefab, transform);
                p.SetPoolOrigin(prefab);
                p.gameObject.SetActive(false);
                queue.Enqueue(p);
            }
        }
    }
}
