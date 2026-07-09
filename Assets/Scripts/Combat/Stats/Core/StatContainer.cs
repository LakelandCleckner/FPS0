using System.Collections.Generic;
using UnityEngine;

namespace Combat.Stats
{
    // Per-entity stat storage + resolution (GDD 14 §3, §7). Upgraded from 2a/2b:
    //   - stores BASE values by index (2a)
    //   - holds registered MODIFIERS per stat (2c)
    //   - Resolve(stat) gathers a stat's modifiers and runs the bucket math (2b)
    //   - caches resolved values with a per-stat VERSION stamp; add/remove bumps the
    //     version and invalidates the cache; reads return cached until it changes
    //
    // Phase 2c: fixed-value modifiers, push registration by handle, cache/version.
    // Derivation + cross-stat invalidation come in 2d.
    public class StatContainer
    {
        // base storage (2a)
        private readonly float[] bases;
        private readonly bool[] hasBase;

        // per-stat modifier lists (index -> its modifiers)
        private readonly List<StatModifier>[] modifiers;

        // per-stat cache + version
        private readonly float[] cachedValue;
        private readonly int[] version;        // bumps on any change to this stat
        private readonly int[] cachedVersion;  // version the cache was computed at
        private readonly bool[] hasCache;

        // scratch list reused during resolution (avoids per-resolve allocation)
        private readonly List<StatModifierValue> scratch = new List<StatModifierValue>(16);

        // custom bucket trees per stat (optional; null = default tree by mode)
        private readonly StatBucketSO[] customTree;

        private static long nextHandleId = 1; // 0 reserved for "None"

        public StatContainer()
        {
            int n = StatRegistry.Count;
            if (n <= 0)
                Debug.LogWarning("[StatContainer] Created before StatRegistry initialized (Count=0). " +
                                 "Ensure StatBootstrap runs first.");
            n = Mathf.Max(0, n);

            bases = new float[n];
            hasBase = new bool[n];
            modifiers = new List<StatModifier>[n];
            cachedValue = new float[n];
            version = new int[n];
            cachedVersion = new int[n];
            hasCache = new bool[n];
            customTree = new StatBucketSO[n];
        }

        // ---- base values (2a) ----

        public void SetBase(StatDefinitionSO stat, float value)
        {
            int i = Idx(stat); if (i < 0) return;
            bases[i] = value; hasBase[i] = true;
            Invalidate(i);
        }

        public void ClearBase(StatDefinitionSO stat)
        {
            int i = Idx(stat); if (i < 0) return;
            bases[i] = 0f; hasBase[i] = false;
            Invalidate(i);
        }

        public float GetBase(StatDefinitionSO stat)
        {
            int i = Idx(stat);
            if (i < 0) return stat != null ? stat.defaultValue : 0f;
            return hasBase[i] ? bases[i] : stat.defaultValue;
        }

        // optional custom tree for a stat
        public void SetCustomTree(StatDefinitionSO stat, StatBucketSO tree)
        {
            int i = Idx(stat); if (i < 0) return;
            customTree[i] = tree;
            Invalidate(i);
        }

        // ---- modifiers (2c) ----

        // Register a modifier; returns its handle (unique id + owner) for removal.
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

        // Precise removal by handle. Idempotent (unknown handle = no-op).
        public bool RemoveModifier(ModifierHandle handle)
        {
            if (!handle.IsValid) return false;
            for (int i = 0; i < modifiers.Length; i++)
            {
                var list = modifiers[i];
                if (list == null) continue;
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j].Handle.Id == handle.Id)
                    {
                        list.RemoveAt(j);
                        Invalidate(i);
                        return true;
                    }
                }
            }
            return false;
        }

        // Bulk removal: every modifier whose handle owner == owner (unequip a weapon,
        // respec a tree). Null owner not bulk-removable (skipped).
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
                {
                    if (ReferenceEquals(list[j].Handle.Owner, owner))
                    {
                        list.RemoveAt(j);
                        removed++;
                        changed = true;
                    }
                }
                if (changed) Invalidate(i);
            }
            return removed;
        }

        // ---- resolution + caching (2c) ----

        // Resolve a stat's final value: base + its modifiers through the bucket math,
        // cached until the stat's version changes.
        public float Resolve(StatDefinitionSO stat)
        {
            int i = Idx(stat);
            if (i < 0) return stat != null ? stat.defaultValue : 0f;

            // cache hit?
            if (hasCache[i] && cachedVersion[i] == version[i])
                return cachedValue[i];

            // gather this stat's modifiers into the scratch value list
            scratch.Clear();
            var list = modifiers[i];
            if (list != null)
                for (int j = 0; j < list.Count; j++)
                    scratch.Add(new StatModifierValue(list[j].BucketId, list[j].Value));

            float baseValue = hasBase[i] ? bases[i] : stat.defaultValue;
            float resolved = StatResolver.Resolve(stat, baseValue, scratch, customTree[i]);

            cachedValue[i] = resolved;
            cachedVersion[i] = version[i];
            hasCache[i] = true;
            return resolved;
        }

        // ---- internals ----

        private void Invalidate(int i)
        {
            version[i]++;      // bump so the cached value is considered stale
            hasCache[i] = false;
        }

        private int Idx(StatDefinitionSO stat)
        {
            int i = StatRegistry.IndexOf(stat);
            if (i < 0)
            {
                Debug.LogError($"[StatContainer] Stat '{(stat != null ? stat.name : "null")}' " +
                               "has no runtime index (not registered / registry not initialized).");
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