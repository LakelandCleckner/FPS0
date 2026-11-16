using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Combat.Info;

namespace Combat.Enemies
{
    // An archetype plus ordered modifier layers, e.g. Grunt + Elite + Fire.
    [CreateAssetMenu(fileName = "EnemyVariant", menuName = "Combat/Enemies/Enemy Variant")]
    public class EnemyVariantSO : ScriptableObject, IDescribable
    {
        [Tooltip("Stable id. Don't change once spawners reference it.")]
        public string id = "";
        [Tooltip("Empty = built from modifier prefixes plus the archetype name.")]
        public string displayNameOverride = "";
        public EnemyArchetypeSO archetype;
        [Tooltip("Applied top to bottom.")]
        public List<EnemyModifierSO> modifiers = new List<EnemyModifierSO>();
        [Tooltip("Hand-tweaked prefab for this combo. Empty = the archetype body.")]
        public GameObject prefabOverride;

        public GameObject Prefab => prefabOverride != null ? prefabOverride : (archetype != null ? archetype.bodyPrefab : null);

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(displayNameOverride)) return displayNameOverride;
                var sb = new StringBuilder();
                foreach (var m in modifiers)
                    if (m != null && !string.IsNullOrEmpty(m.namePrefix)) sb.Append(m.namePrefix).Append(' ');
                sb.Append(archetype != null ? archetype.StatSourceLabel : name);
                return sb.ToString();
            }
        }

        // Every layer in order, each under its name.
        public void Describe(Describer d)
        {
            foreach (var m in modifiers)
            {
                if (m == null) continue;
                d.Line().Add(d.Context.Rich ? "<b>" : "").Add(m.StatSourceLabel).Add(d.Context.Rich ? "</b>" : "");
                m.Describe(d);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < modifiers.Count; i++)
                for (int j = i + 1; j < modifiers.Count; j++)
                    if (modifiers[i] != null && modifiers[i] == modifiers[j])
                        Debug.LogWarning($"[EnemyVariant] '{name}' lists '{modifiers[i].name}' twice.", this);
        }
#endif
    }
}
