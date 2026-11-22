using System.Collections.Generic;
using UnityEngine;

namespace Combat.Enemies
{
    // Rank as a layer: Minor is no tier layer, higher ranks are the tier modifier that raises to them.
    public static class EnemyTierLayers
    {
        public static readonly string[] RankNames = { "Minor", "Major", "Elite", "Miniboss" };

        // Rank set by the tier layers in a list; Minor when there are none.
        public static EnemyRank RankIn(IReadOnlyList<EnemyModifierSO> layers)
        {
            var rank = EnemyRank.Minor;
            if (layers == null) return rank;
            for (int i = 0; i < layers.Count; i++)
            {
                var m = layers[i];
                if (m != null && m.kind == EnemyModifierKind.Tier && (int)m.raiseRankTo > (int)rank) rank = (EnemyRank)m.raiseRankTo;
            }
            return rank;
        }

        // Swaps the tier layers in a list for the one given (null = Minor), kept first.
        public static void SetTier(List<EnemyModifierSO> layers, EnemyModifierSO tier)
        {
            layers.RemoveAll(m => m == null || m.kind == EnemyModifierKind.Tier);
            if (tier != null) layers.Insert(0, tier);
        }

        // Tier layer first, then the rest; nulls and repeats skipped.
        public static List<EnemyModifierSO> Compose(EnemyModifierSO tier, IEnumerable<EnemyModifierSO> rest)
        {
            var list = new List<EnemyModifierSO>();
            if (tier != null) list.Add(tier);
            if (rest != null)
                foreach (var m in rest)
                    if (m != null && !list.Contains(m)) list.Add(m);
            return list;
        }

#if UNITY_EDITOR
        private static Dictionary<EnemyRank, EnemyModifierSO> byRank;

        // The tier modifier for a rank, from the project; null for Minor or when none exists.
        public static EnemyModifierSO For(EnemyRank rank)
        {
            if (rank == EnemyRank.Minor) return null;
            if (byRank == null) Load();
            return byRank.TryGetValue(rank, out var m) ? m : null;
        }

        // Call after tier assets change.
        public static void Invalidate() => byRank = null;

        private static void Load()
        {
            byRank = new Dictionary<EnemyRank, EnemyModifierSO>();
            var all = new List<EnemyModifierSO>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:EnemyModifierSO"))
            {
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyModifierSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.kind == EnemyModifierKind.Tier && m.raiseRankTo != EnemyRankRaise.Keep) all.Add(m);
            }
            all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var m in all)
            {
                var r = (EnemyRank)m.raiseRankTo;
                if (!byRank.ContainsKey(r)) byRank[r] = m;
            }
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void Hook() => UnityEditor.EditorApplication.projectChanged += Invalidate;
#endif
    }
}
