using System.Collections.Generic;
using UnityEngine;
using Combat.Enemies;

namespace Combat.AI
{
    // A squad to spawn: which enemies (archetype plus layers, or a hand-made variant), how many of each, who leads.
    [CreateAssetMenu(fileName = "Squad", menuName = "Combat/Enemies/Enemy Squad")]
    public class EnemySquadSO : ScriptableObject
    {
        [System.Serializable]
        public struct Member
        {
            [Tooltip("Hand-made enemy. Takes priority over Archetype.")]
            public EnemyVariantSO variant;
            [Tooltip("Used when Variant is empty.")]
            public EnemyArchetypeSO archetype;
            [Tooltip("Layers on top: rank tier, element, shield.")]
            public List<EnemyModifierSO> extraModifiers;
            [Min(1)] public int count;
            [Tooltip("The first of these leads. With no leader ticked, the highest rank leads.")]
            public bool leader;

            public EnemySpawnDef Def => new EnemySpawnDef(variant, archetype, extraModifiers);
            public bool IsSet => Def.IsSet;
        }

        [Tooltip("Stable id. Don't change once spawners reference it.")]
        public string id = "";
        public string displayName = "";
        public List<Member> members = new List<Member>();
        [Tooltip("Members are scattered within this radius of the spawn point (m).")]
        [Min(0f)] public float spread = 3f;

        public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;

        // Encounter budget cost: each member by rank (Minor 1, Major 2, Elite 3, Miniboss 5, Boss 8)
        private static readonly int[] RankCost = { 1, 2, 3, 5, 8 };

        public int Cost
        {
            get
            {
                int c = 0;
                foreach (var m in members)
                {
                    if (!m.IsSet) continue;
                    int rank = (int)m.Def.Rank;
                    c += RankCost[Mathf.Clamp(rank, 0, RankCost.Length - 1)] * Mathf.Max(1, m.count);
                }
                return c;
            }
        }

        public int TotalCount
        {
            get
            {
                int n = 0;
                foreach (var m in members) if (m.IsSet) n += Mathf.Max(1, m.count);
                return n;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            int leaders = 0;
            foreach (var m in members) if (m.leader) leaders++;
            if (leaders > 1) Debug.LogWarning($"[EnemySquad] '{name}' ticks more than one leader; the first is used.", this);
        }
#endif
    }
}
