using System.Collections.Generic;
using UnityEngine;
using Combat.Enemies;

namespace Combat.AI
{
    // A squad to spawn: which variants, how many of each, who leads.
    [CreateAssetMenu(fileName = "Squad", menuName = "Combat/Enemies/Enemy Squad")]
    public class EnemySquadSO : ScriptableObject
    {
        [System.Serializable]
        public struct Member
        {
            public EnemyVariantSO variant;
            [Min(1)] public int count;
            [Tooltip("The first of these leads. With no leader ticked, the highest rank leads.")]
            public bool leader;
        }

        [Tooltip("Stable id. Don't change once spawners reference it.")]
        public string id = "";
        public string displayName = "";
        public List<Member> members = new List<Member>();
        [Tooltip("Members are scattered within this radius of the spawn point (m).")]
        [Min(0f)] public float spread = 3f;

        public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;

        public int TotalCount
        {
            get
            {
                int n = 0;
                foreach (var m in members) if (m.variant != null) n += Mathf.Max(1, m.count);
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
