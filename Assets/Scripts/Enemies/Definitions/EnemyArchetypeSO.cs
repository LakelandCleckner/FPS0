using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Loot;
using Combat.Stats;

namespace Combat.Enemies
{
    // A base enemy: body, every base stat in one place, default loot and attacks.
    [CreateAssetMenu(fileName = "EnemyArchetype", menuName = "Combat/Enemies/Enemy Archetype")]
    public class EnemyArchetypeSO : ScriptableObject, IStatSourceLabel
    {
        [Header("Identity")]
        [Tooltip("Stable id. Don't change once spawners reference it.")]
        public string id = "";
        public string displayName = "";
        [Tooltip("Enemy prefab with an EnemyDefinitionApplier on its root.")]
        public GameObject bodyPrefab;

        [Header("Base Stats")]
        [Tooltip("Health, movement, attack and resist bases. Overrides the same stats on the prefab's components.")]
        public List<CombatantStats.BaseStat> baseStats = new List<CombatantStats.BaseStat>();

        [Header("Melee")]
        [Tooltip("Empty = the melee component's own damage type.")]
        public DamageTypeSO meleeDamageType;
        public List<HitEffectSO> meleeHitEffects = new List<HitEffectSO>();

        [Header("Behaviour")]
        [Tooltip("How it fights. Empty = plain melee. Modifiers can override it.")]
        public Combat.AI.EnemyBehaviourProfileSO behaviourProfile;

        [Header("Death")]
        [Tooltip("Blasts or hazards left behind on death.")]
        public List<EnemyDeathEffectSO> deathEffects = new List<EnemyDeathEffectSO>();

        [Header("Affixes")]
        public List<EnemyAffixSO> affixes = new List<EnemyAffixSO>();

        [Header("Presentation")]
        [Tooltip("Base rank. Tier modifiers can raise it.")]
        public EnemyRank rank = EnemyRank.Minor;

        [Header("Loot")]
        [Tooltip("Empty = the loot dropper's own table.")]
        public LootTableSO lootTable;

        public string StatSourceLabel => string.IsNullOrEmpty(displayName) ? name : displayName;

        public bool DefinesBase(StatDefinitionSO stat)
        {
            for (int i = 0; i < baseStats.Count; i++)
                if (baseStats[i].stat == stat) return true;
            return false;
        }
    }
}
