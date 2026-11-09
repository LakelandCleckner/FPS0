using System.Collections.Generic;
using UnityEngine;
using Combat.Enemies;

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

        // One enemy definition: a variant, or a plain archetype when no variant is set.
        [System.Serializable]
        public struct VariantEntry
        {
            public EnemyVariantSO variant;
            [Tooltip("Used when Variant is empty: the archetype with no modifier layers.")]
            public EnemyArchetypeSO archetype;
            [Tooltip("Instances created at scene start so first spawns don't hitch.")]
            [Min(0)] public int prewarm;
        }

        [Header("Registered enemy types")]
        [SerializeField] private EnemyType[] types;

        [Header("Registered variants (one pool each)")]
        [Tooltip("Spawning a variant that isn't listed still works; its pool is created on first use.")]
        [SerializeField] private VariantEntry[] variants;

        [Tooltip("Inactive pooled instances are parented here. Leave empty to use this " +
                 "spawner's own transform.")]
        [SerializeField] private Transform poolParent;

        private readonly Dictionary<string, EnemyPool> poolsById
            = new Dictionary<string, EnemyPool>();
        private readonly Dictionary<Object, string> idByDefinition = new Dictionary<Object, string>();
        private readonly Dictionary<string, Object> definitionById = new Dictionary<string, Object>();
        private readonly HashSet<Object> failed = new HashSet<Object>();
        private Transform PoolParent => poolParent != null ? poolParent : transform;

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

            if (variants != null)
                foreach (var v in variants)
                    Register(v.variant != null ? v.variant : (Object)v.archetype);
        }

        // Prewarm after every Awake, so the scene's GOAP manager exists for new instances.
        private void Start()
        {
            if (variants == null) return;
            foreach (var v in variants)
            {
                if (v.prewarm <= 0) continue;
                var id = Register(v.variant != null ? v.variant : (Object)v.archetype);
                if (id != null) poolsById[id].Prewarm(v.prewarm);
            }
        }

        // ---- variants ----

        // Spawns a variant from its own pool; an unlisted variant gets a pool on first use.
        public EnemyPoolHandle Spawn(EnemyVariantSO variant, Vector3 position, Quaternion rotation)
        {
            var id = Register(variant);
            return id != null ? Spawn(id, position, rotation) : null;
        }

        public EnemyPoolHandle Spawn(EnemyVariantSO variant, Vector3 position)
            => Spawn(variant, position, Quaternion.identity);

        // A plain archetype, no modifier layers.
        public EnemyPoolHandle Spawn(EnemyArchetypeSO archetype, Vector3 position, Quaternion rotation)
        {
            var id = Register(archetype);
            return id != null ? Spawn(id, position, rotation) : null;
        }

        public List<EnemyPoolHandle> SpawnGroup(EnemyVariantSO variant, int count, Vector3 center, float radius)
        {
            var id = Register(variant);
            return id != null ? SpawnGroup(id, count, center, radius) : new List<EnemyPoolHandle>();
        }

        // Pool id for a variant or archetype, creating its pool once. Null when it can't spawn.
        private string Register(Object definition)
        {
            if (definition == null || failed.Contains(definition)) return null;
            if (idByDefinition.TryGetValue(definition, out var known)) return known;
            failed.Add(definition);   // cleared below once the pool exists; failures log once

            var variant = definition as EnemyVariantSO;
            var archetype = variant != null ? variant.archetype : definition as EnemyArchetypeSO;
            var prefab = variant != null ? variant.Prefab : archetype != null ? archetype.bodyPrefab : null;
            string label = variant != null ? variant.DisplayName : archetype != null ? archetype.StatSourceLabel : definition.name;

            if (archetype == null) { Debug.LogError($"[EnemySpawner] '{definition.name}' has no archetype.", definition); return null; }
            if (prefab == null) { Debug.LogError($"[EnemySpawner] '{definition.name}' has no body prefab.", definition); return null; }
            if (prefab.GetComponent<EnemyDefinitionApplier>() == null)
            {
                Debug.LogError($"[EnemySpawner] '{prefab.name}' has no EnemyDefinitionApplier, so '{definition.name}' can't apply.", prefab);
                return null;
            }

            string id = variant != null ? variant.id : archetype.id;
            if (string.IsNullOrEmpty(id))
            {
                id = definition.name;
                Debug.LogWarning($"[EnemySpawner] '{definition.name}' has no id; using its asset name.", definition);
            }
            if (poolsById.ContainsKey(id))
            {
                var other = definitionById.TryGetValue(id, out var o) ? o : null;
                Debug.LogError($"[EnemySpawner] Id '{id}' on '{definition.name}' is already used by '{(other != null ? other.name : "an enemy type")}'.", definition);
                return null;
            }

            poolsById[id] = new EnemyPool(prefab, PoolParent, go =>
            {
                go.name = label;
                var applier = go.GetComponent<EnemyDefinitionApplier>();
                if (variant != null) applier.SetVariant(variant);
                else applier.SetArchetype(archetype);
            });
            failed.Remove(definition);
            idByDefinition[definition] = id;
            definitionById[id] = definition;
            return id;
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
            var adapter = handle.GameObject.GetComponent<EnemyPoolAdapter>();
            if (adapter != null)
                adapter.Bind(this, typeId, handle);

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