using System.Collections.Generic;
using UnityEngine;
using Combat.Enemies;

namespace Combat.Spawning
{
    // Owns one pool per body prefab and spawns enemy definitions (archetype plus layers) from them.
    //   Spawn      - one enemy at an exact position; every other form is built on it.
    //   SpawnGroup - several of one definition scattered around a point, fighting as a squad.
    //   SpawnSquad - a mixed squad asset.
    public class EnemySpawner : MonoBehaviour
    {
        // A body to pool ahead of time: an archetype, or a hand-made variant.
        [System.Serializable]
        public struct VariantEntry
        {
            [Tooltip("Hand-made enemy. Takes priority over Archetype.")]
            public EnemyVariantSO variant;
            [Tooltip("Used when Variant is empty.")]
            public EnemyArchetypeSO archetype;
            [Tooltip("Instances created at scene start so first spawns don't hitch.")]
            [Min(0)] public int prewarm;
        }

        [Header("Bodies to prewarm (one pool per body)")]
        [Tooltip("Spawning anything unlisted still works; its body's pool is created on first use. Prewarm adds to the body's pool.")]
        [SerializeField] private VariantEntry[] variants;

        [Tooltip("Inactive pooled instances are parented here. Leave empty to use this " +
                 "spawner's own transform.")]
        [SerializeField] private Transform poolParent;

        private readonly Dictionary<string, EnemyPool> poolsById
            = new Dictionary<string, EnemyPool>();
        private readonly Dictionary<GameObject, string> idByBody = new Dictionary<GameObject, string>();
        private readonly HashSet<Object> failed = new HashSet<Object>();
        private Transform PoolParent => poolParent != null ? poolParent : transform;

        private void Awake()
        {
            if (variants != null)
                foreach (var v in variants)
                    RegisterBody(new EnemySpawnDef(v.variant, v.archetype));
        }

        // Prewarm after every Awake, so the scene's GOAP manager exists for new instances.
        private void Start()
        {
            if (variants == null) return;
            foreach (var v in variants)
            {
                if (v.prewarm <= 0) continue;
                var id = RegisterBody(new EnemySpawnDef(v.variant, v.archetype));
                if (id != null) poolsById[id].Prewarm(v.prewarm);
            }
        }

        // ---- definitions ----

        // A preset or archetype plus extra layers, from one pool per body prefab.
        public EnemyPoolHandle Spawn(EnemySpawnDef def, Vector3 position, Quaternion rotation)
        {
            var id = RegisterBody(def);
            if (id == null) return null;
            string label = def.DisplayName;
            return Spawn(id, position, rotation, go =>
            {
                go.name = label;
                go.GetComponent<EnemyDefinitionApplier>().SetDefinition(def);
            });
        }

        public EnemyPoolHandle Spawn(EnemySpawnDef def, Vector3 position)
            => Spawn(def, position, Quaternion.identity);

        public EnemyPoolHandle Spawn(EnemyVariantSO variant, Vector3 position, Quaternion rotation)
            => Spawn(new EnemySpawnDef(variant), position, rotation);

        public EnemyPoolHandle Spawn(EnemyVariantSO variant, Vector3 position)
            => Spawn(new EnemySpawnDef(variant), position, Quaternion.identity);

        // A plain archetype, no modifier layers.
        public EnemyPoolHandle Spawn(EnemyArchetypeSO archetype, Vector3 position, Quaternion rotation)
            => Spawn(new EnemySpawnDef(null, archetype), position, rotation);

        public List<EnemyPoolHandle> SpawnGroup(EnemySpawnDef def, int count, Vector3 center, float radius)
        {
            var spawned = new List<EnemyPoolHandle>(count);
            for (int i = 0; i < count; i++)
            {
                var h = Spawn(def, center + ScatterOffset(radius));
                if (h != null) spawned.Add(h);
            }
            if (spawned.Count > 1) Combat.AI.EnemySquad.Form(spawned);
            return spawned;
        }

        public List<EnemyPoolHandle> SpawnGroup(EnemyVariantSO variant, int count, Vector3 center, float radius)
            => SpawnGroup(new EnemySpawnDef(variant), count, center, radius);

        // Pool id for a definition's body, creating the pool once. Null when it can't spawn.
        private string RegisterBody(EnemySpawnDef def)
        {
            Object key = def.preset != null ? def.preset : (Object)def.archetype;
            if (key == null) return null;
            var prefab = def.Prefab;
            if (prefab != null && idByBody.TryGetValue(prefab, out var known)) return known;
            if (failed.Contains(key)) return null;
            failed.Add(key);   // removed below once the pool exists; failures log once

            if (def.Archetype == null) { Debug.LogError($"[EnemySpawner] '{key.name}' has no archetype.", key); return null; }
            if (prefab == null) { Debug.LogError($"[EnemySpawner] '{key.name}' has no body prefab.", key); return null; }
            if (prefab.GetComponent<EnemyDefinitionApplier>() == null)
            {
                Debug.LogError($"[EnemySpawner] '{prefab.name}' has no EnemyDefinitionApplier, so '{key.name}' can't apply.", prefab);
                return null;
            }

            string id = "body/" + prefab.name + "/" + prefab.GetInstanceID();
            poolsById[id] = new EnemyPool(prefab, PoolParent);
            idByBody[prefab] = id;
            failed.Remove(key);
            return id;
        }

        // THE PRIMITIVE - one enemy of a type at a position. Returns the handle so a
        // caller (a squad, an encounter) can track what it spawned. Null if the type
        // id isn't registered.
        public EnemyPoolHandle Spawn(string typeId, Vector3 position, Quaternion rotation, System.Action<GameObject> prepare = null)
        {
            if (!poolsById.TryGetValue(typeId, out var pool))
            {
                Debug.LogError($"[EnemySpawner] No enemy type registered for id '{typeId}'.");
                return null;
            }

            var handle = pool.Rent(position, rotation, prepare);

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
        // inside each other. Random-within-radius placement - the simplest strategy;
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
            // A group fights as a squad
            if (spawned.Count > 1) Combat.AI.EnemySquad.Form(spawned);
            return spawned;
        }

        // A mixed squad from a squad asset, scattered around a point; the ticked member leads.
        public List<EnemyPoolHandle> SpawnSquad(Combat.AI.EnemySquadSO squad, Vector3 center)
        {
            var spawned = new List<EnemyPoolHandle>();
            if (squad == null) return spawned;
            var brains = new List<Combat.AI.EnemyBrain>();
            Combat.AI.EnemyBrain leader = null;
            foreach (var m in squad.members)
            {
                if (!m.IsSet) continue;
                var def = m.Def;
                for (int i = 0; i < Mathf.Max(1, m.count); i++)
                {
                    var h = Spawn(def, center + ScatterOffset(squad.spread));
                    if (h == null) continue;
                    spawned.Add(h);
                    var b = h.GameObject.GetComponent<Combat.AI.EnemyBrain>();
                    if (b == null) continue;
                    brains.Add(b);
                    if (m.leader && leader == null) leader = b;
                }
            }
            Combat.AI.EnemySquad.Form(brains, leader, squad.Label);
            return spawned;
        }

        // Return a spawned enemy to its pool. The caller (or the enemy's own death
        // handler) invokes this AFTER any on-death reactions have run - the pool's
        // Return then resets and deactivates, in that order.
        public void Despawn(string typeId, EnemyPoolHandle handle)
        {
            if (handle == null) return;
            if (poolsById.TryGetValue(typeId, out var pool))
                pool.Return(handle);
            else
                Debug.LogError($"[EnemySpawner] Can't despawn - no pool for id '{typeId}'.");
        }

        // Random horizontal offset within the radius, on the same Y as the center so
        // enemies don't spawn in the air or in the floor. NavMesh sampling would make
        // this robust on uneven ground - a later refinement.
        private static Vector3 ScatterOffset(float radius)
        {
            if (radius <= 0f) return Vector3.zero;
            Vector2 c = Random.insideUnitCircle * radius;
            return new Vector3(c.x, 0f, c.y);
        }
    }
}