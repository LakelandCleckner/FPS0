using UnityEngine;

namespace Combat.Stats
{
    // Per-entity stat storage. Every entity that has stats (player, weapon, enemy)
    // owns one. Phase 2a scope: stores BASE values by runtime index and reads them
    // back (with the StatDefinitionSO default when unset). Modifiers, buckets, and
    // resolution arrive in later phases — this is the storage substrate they build on.
    //
    // Not a MonoBehaviour — a plain class an owner (component) holds, so weapons
    // (plain data) and scene entities can both own one.
    public class StatContainer
    {
        private readonly float[] bases;
        private readonly bool[] hasBase;

        public StatContainer()
        {
            int n = StatRegistry.Count;
            if (n <= 0)
                Debug.LogWarning("[StatContainer] Created before StatRegistry initialized (Count=0). " +
                                 "Ensure StatBootstrap runs first.");
            bases = new float[Mathf.Max(0, n)];
            hasBase = new bool[Mathf.Max(0, n)];
        }

        public void SetBase(StatDefinitionSO stat, float value)
        {
            int i = StatRegistry.IndexOf(stat);
            if (!ValidIndex(i, stat)) return;
            bases[i] = value;
            hasBase[i] = true;
        }

        public void ClearBase(StatDefinitionSO stat)
        {
            int i = StatRegistry.IndexOf(stat);
            if (!ValidIndex(i, stat)) return;
            bases[i] = 0f;
            hasBase[i] = false;
        }

        // Get the base value (the definition's default if none was set). In later
        // phases GetBase stays the raw base; a separate Resolve() will apply
        // modifiers + buckets + clamps on top of this.
        public float GetBase(StatDefinitionSO stat)
        {
            int i = StatRegistry.IndexOf(stat);
            if (!ValidIndex(i, stat)) return stat != null ? stat.defaultValue : 0f;
            return hasBase[i] ? bases[i] : stat.defaultValue;
        }

        public bool HasBase(StatDefinitionSO stat)
        {
            int i = StatRegistry.IndexOf(stat);
            if (i < 0 || i >= hasBase.Length) return false;
            return hasBase[i];
        }

        private bool ValidIndex(int i, StatDefinitionSO stat)
        {
            if (i < 0)
            {
                Debug.LogError($"[StatContainer] Stat '{(stat != null ? stat.name : "null")}' " +
                               "has no runtime index (not registered / registry not initialized).");
                return false;
            }
            if (i >= bases.Length)
            {
                Debug.LogError($"[StatContainer] Stat index {i} out of range (container sized {bases.Length}). " +
                               "Container built before all stats were registered?");
                return false;
            }
            return true;
        }
    }
}
