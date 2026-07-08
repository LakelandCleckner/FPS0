using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Combat.Stats
{
    // Central registry of all StatDefinitionSO assets. On initialization it collects
    // every stat definition and assigns each a deterministic runtime INDEX (sorted
    // by id, so indices are stable across runs). Storage/lookup elsewhere use the
    // index for array-fast access; authoring uses the SO reference.
    //
    // Phase 2a: the registry + indexing. It exposes Count (how big a stat array must
    // be) and index<->definition lookups.
    public static class StatRegistry
    {
        private static StatDefinitionSO[] byIndex;
        private static Dictionary<string, StatDefinitionSO> byId;
        private static bool initialized;

        public static bool IsInitialized => initialized;

        // Number of registered stats == required size of any per-stat array.
        public static int Count => byIndex != null ? byIndex.Length : 0;

        // Initialize from an explicit set of definitions (deterministic order by id).
        // Assigns each definition its RuntimeIndex. Safe to call again to rebuild.
        public static void Initialize(IEnumerable<StatDefinitionSO> definitions)
        {
            var list = definitions
                .Where(d => d != null)
                .Distinct()
                .OrderBy(d => d.id, System.StringComparer.Ordinal)
                .ToArray();

            byIndex = list;
            byId = new Dictionary<string, StatDefinitionSO>(list.Length);

            for (int i = 0; i < list.Length; i++)
            {
                list[i].RuntimeIndex = i;
                if (string.IsNullOrEmpty(list[i].id))
                    Debug.LogWarning($"[StatRegistry] Stat definition '{list[i].name}' has an empty id.");
                else if (byId.ContainsKey(list[i].id))
                    Debug.LogError($"[StatRegistry] Duplicate stat id '{list[i].id}'.");
                else
                    byId[list[i].id] = list[i];
            }

            initialized = true;
            Debug.Log($"[StatRegistry] Initialized with {list.Length} stats.");
        }

        public static StatDefinitionSO GetByIndex(int index)
        {
            if (byIndex == null || index < 0 || index >= byIndex.Length) return null;
            return byIndex[index];
        }

        public static StatDefinitionSO GetById(string id)
        {
            if (byId != null && byId.TryGetValue(id, out var def)) return def;
            return null;
        }

        // Resolve a definition's index. -1 if unregistered.
        public static int IndexOf(StatDefinitionSO def)
        {
            return def != null ? def.RuntimeIndex : -1;
        }
    }
}
