using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Combat.Enemies
{
    // What to spawn: a preset variant or a plain archetype, plus modifier layers added on top.
    [System.Serializable]
    public struct EnemySpawnDef
    {
        [Tooltip("Hand-made combo. Takes priority over Archetype.")]
        public EnemyVariantSO preset;
        [Tooltip("Used when Preset is empty.")]
        public EnemyArchetypeSO archetype;
        [Tooltip("Layers added after the preset's own, e.g. a shield.")]
        public List<EnemyModifierSO> modifiers;

        public EnemySpawnDef(EnemyVariantSO preset, EnemyArchetypeSO archetype = null, List<EnemyModifierSO> modifiers = null)
        {
            this.preset = preset;
            this.archetype = archetype;
            this.modifiers = modifiers;
        }

        public EnemyArchetypeSO Archetype => preset != null ? preset.archetype : archetype;
        public GameObject Prefab => preset != null ? preset.Prefab : archetype != null ? archetype.bodyPrefab : null;
        public bool IsSet => Archetype != null;

        // Preset layers first, then the extras; nulls and repeats skipped.
        public void CollectLayers(List<EnemyModifierSO> into)
        {
            into.Clear();
            if (preset != null) Add(into, preset.modifiers);
            Add(into, modifiers);
        }

        public EnemyRank Rank
        {
            get
            {
                var list = new List<EnemyModifierSO>();
                CollectLayers(list);
                return RankOf(Archetype, list);
            }
        }

        public string DisplayName
        {
            get
            {
                var list = new List<EnemyModifierSO>();
                CollectLayers(list);
                return NameOf(Archetype, list, preset != null ? preset.displayNameOverride : null);
            }
        }

        public static void Add(List<EnemyModifierSO> into, List<EnemyModifierSO> from)
        {
            if (from == null) return;
            foreach (var m in from)
                if (m != null && !into.Contains(m)) into.Add(m);
        }

        // Archetype rank, raised by the highest layer.
        public static EnemyRank RankOf(EnemyArchetypeSO arch, IReadOnlyList<EnemyModifierSO> layers)
        {
            var rank = arch != null ? arch.rank : EnemyRank.Minor;
            if (layers != null)
                for (int i = 0; i < layers.Count; i++)
                    if (layers[i] != null && (int)layers[i].raiseRankTo > (int)rank) rank = (EnemyRank)layers[i].raiseRankTo;
            return rank;
        }

        // Override, else layer prefixes plus the archetype's name for the rank. Tier prefixes drop when the rank has its own name.
        public static string NameOf(EnemyArchetypeSO arch, IReadOnlyList<EnemyModifierSO> layers, string nameOverride)
        {
            if (!string.IsNullOrEmpty(nameOverride)) return nameOverride;
            if (arch == null) return "";
            var rank = RankOf(arch, layers);
            bool ranked = arch.HasRankName(rank);
            var sb = new StringBuilder();
            if (layers != null)
                for (int i = 0; i < layers.Count; i++)
                {
                    var m = layers[i];
                    if (m == null || string.IsNullOrEmpty(m.namePrefix)) continue;
                    if (ranked && m.kind == EnemyModifierKind.Tier) continue;
                    sb.Append(m.namePrefix).Append(' ');
                }
            sb.Append(arch.NameFor(rank));
            return sb.ToString();
        }
    }
}
