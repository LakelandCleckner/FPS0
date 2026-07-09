using System.Collections.Generic;
using UnityEngine;

namespace Combat.Stats
{
    // Per-entity stat storage + resolution (GDD 14 §3, §6, §7). Phase 2d adds:
    //   - DERIVED modifiers (value = coeff × resolved(sourceStat), optionally capped)
    //   - lazy-recursive resolution with a CYCLE GUARD
    //   - CROSS-STAT invalidation via version stamps: a cached value records the
    //     versions of the source stats it depended on and invalidates if any moved
    //     (local + lazy; no reverse-dependency graph)
    //
    // Derived modifiers may read stats in THIS container. (Cross-CONTAINER derivation
    // — e.g. a stack reading its source's stats — is handled at the tick/consumer
    // level in later phases; the container-local case is the engine core.)
    public class StatContainer
    {
        private readonly float[] bases;
        private readonly bool[] hasBase;
        private readonly List<StatModifier>[] modifiers;
        private readonly StatBucketSO[] customTree;

        // cache + version
        private readonly float[] cachedValue;
        private readonly int[] version;         // bumps on any direct change to this stat
        private readonly int[] cachedVersion;   // this stat's own version at cache time
        private readonly bool[] hasCache;

        // cross-stat dependency tracking: for a cached stat, which source stats it
        // read and their versions at cache time. If any moved, the cache is stale.
        private readonly List<(int sourceIndex, int sourceVersion)>[] cacheDeps;

        // cycle guard: stats currently mid-resolution
        private readonly bool[] resolving;

        private readonly List<StatModifierValue> scratch = new List<StatModifierValue>(16);
        private static long nextHandleId = 1;

        public StatContainer()
        {
            int n = Mathf.Max(0, StatRegistry.Count);
            if (StatRegistry.Count <= 0)
                Debug.LogWarning("[StatContainer] Created before StatRegistry initialized. Ensure StatBootstrap runs first.");

            bases = new float[n];
            hasBase = new bool[n];
            modifiers = new List<StatModifier>[n];
            customTree = new StatBucketSO[n];
            cachedValue = new float[n];
            version = new int[n];
            cachedVersion = new int[n];
            hasCache = new bool[n];
            cacheDeps = new List<(int, int)>[n];
            resolving = new bool[n];
        }

        // ---- base ----
        public void SetBase(StatDefinitionSO stat, float value)
        {
            int i = Idx(stat); if (i < 0) return;
            bases[i] = value; hasBase[i] = true; Invalidate(i);
        }
        public void ClearBase(StatDefinitionSO stat)
        {
            int i = Idx(stat); if (i < 0) return;
            bases[i] = 0f; hasBase[i] = false; Invalidate(i);
        }
        public float GetBase(StatDefinitionSO stat)
        {
            int i = Idx(stat);
            if (i < 0) return stat != null ? stat.defaultValue : 0f;
            return hasBase[i] ? bases[i] : stat.defaultValue;
        }
        public void SetCustomTree(StatDefinitionSO stat, StatBucketSO tree)
        {
            int i = Idx(stat); if (i < 0) return;
            customTree[i] = tree; Invalidate(i);
        }

        // ---- modifiers ----
        public ModifierHandle AddModifier(StatModifier mod, object owner = null)
        {
            if (mod == null || mod.TargetStat == null)
            {
                Debug.LogError("[StatContainer] AddModifier: null modifier or target stat.");
                return ModifierHandle.None;
            }
            int i = Idx(mod.TargetStat);
            if (i < 0) return ModifierHandle.None;

            var handle = new ModifierHandle(nextHandleId++, owner);
            mod.Handle = handle;
            (modifiers[i] ??= new List<StatModifier>(4)).Add(mod);
            Invalidate(i);
            return handle;
        }

        public bool RemoveModifier(ModifierHandle handle)
        {
            if (!handle.IsValid) return false;
            for (int i = 0; i < modifiers.Length; i++)
            {
                var list = modifiers[i];
                if (list == null) continue;
                for (int j = 0; j < list.Count; j++)
                    if (list[j].Handle.Id == handle.Id)
                    {
                        list.RemoveAt(j);
                        Invalidate(i);
                        return true;
                    }
            }
            return false;
        }

        public int RemoveAllFromOwner(object owner)
        {
            if (owner == null) return 0;
            int removed = 0;
            for (int i = 0; i < modifiers.Length; i++)
            {
                var list = modifiers[i];
                if (list == null) continue;
                bool changed = false;
                for (int j = list.Count - 1; j >= 0; j--)
                    if (ReferenceEquals(list[j].Handle.Owner, owner))
                    {
                        list.RemoveAt(j); removed++; changed = true;
                    }
                if (changed) Invalidate(i);
            }
            return removed;
        }

        // ---- resolution ----
        public float Resolve(StatDefinitionSO stat)
        {
            int i = Idx(stat);
            if (i < 0) return stat != null ? stat.defaultValue : 0f;
            return ResolveIndex(i, stat);
        }

        private float ResolveIndex(int i, StatDefinitionSO stat)
        {
            // cycle guard: already resolving this stat -> break the loop
            if (resolving[i])
            {
                Debug.LogWarning($"[StatContainer] Cycle detected resolving '{stat.id}'. " +
                                 "A derived modifier forms a loop; contribution treated as 0.");
                return hasBase[i] ? bases[i] : stat.defaultValue; // best-effort, no recurse
            }

            // cache valid? (own version unchanged AND all dependency versions unchanged)
            if (hasCache[i] && cachedVersion[i] == version[i] && DepsStillValid(i))
                return cachedValue[i];

            resolving[i] = true;

            scratch.Clear();
            var deps = cacheDeps[i];
            if (deps == null) { deps = new List<(int, int)>(); cacheDeps[i] = deps; }
            deps.Clear();

            var list = modifiers[i];
            if (list != null)
            {
                for (int j = 0; j < list.Count; j++)
                {
                    var m = list[j];
                    float val;
                    if (!m.IsDerived)
                    {
                        val = m.Value;
                    }
                    else
                    {
                        // derived: coeff × resolved(sourceStat), optionally capped.
                        int si = StatRegistry.IndexOf(m.SourceStat);
                        if (si < 0)
                        {
                            val = 0f;
                        }
                        else
                        {
                            float sourceVal = ResolveIndex(si, m.SourceStat);
                            val = m.Coefficient * sourceVal;
                            if (m.UseCap && val > m.Cap) val = m.Cap;
                            // record the dependency (source index + its version at read)
                            deps.Add((si, version[si]));
                        }
                    }
                    scratch.Add(new StatModifierValue(m.BucketId, val));
                }
            }

            float baseValue = hasBase[i] ? bases[i] : stat.defaultValue;
            float resolved = StatResolver.Resolve(stat, baseValue, scratch, customTree[i]);

            cachedValue[i] = resolved;
            cachedVersion[i] = version[i];
            hasCache[i] = true;
            resolving[i] = false;
            return resolved;
        }

        // Are all recorded dependency versions still current?
        private bool DepsStillValid(int i)
        {
            var deps = cacheDeps[i];
            if (deps == null || deps.Count == 0) return true;
            for (int k = 0; k < deps.Count; k++)
                if (version[deps[k].sourceIndex] != deps[k].sourceVersion)
                    return false;
            return true;
        }

        // ---- internals ----
        private void Invalidate(int i)
        {
            version[i]++;
            hasCache[i] = false;
        }

        // The current version of a stat (bumps on any base/modifier/tree change to it).
        // Consumers cache a resolved value alongside the version it was built at, and
        // recompute when the version moves. -1 if the stat isn't registered.
        public int GetVersion(StatDefinitionSO stat)
        {
            int i = StatRegistry.IndexOf(stat);
            if (i < 0 || i >= version.Length) return -1;
            return version[i];
        }


        private int Idx(StatDefinitionSO stat)
        {
            int i = StatRegistry.IndexOf(stat);
            if (i < 0)
            {
                Debug.LogError($"[StatContainer] Stat '{(stat != null ? stat.name : "null")}' has no runtime index.");
                return -1;
            }
            if (i >= bases.Length)
            {
                Debug.LogError($"[StatContainer] Stat index {i} out of range (sized {bases.Length}).");
                return -1;
            }
            return i;
        }
    }
}