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
        [Tooltip("Flinch and stagger tuning. Empty = the hit reaction component's default.")]
        public HitReactionProfileSO hitReactionProfile;

        [Header("Death")]
        [Tooltip("Blasts or hazards left behind on death.")]
        public List<EnemyDeathEffectSO> deathEffects = new List<EnemyDeathEffectSO>();

        [Header("Affixes")]
        public List<EnemyAffixSO> affixes = new List<EnemyAffixSO>();

        [Header("Presentation")]
        [Tooltip("Base rank. Tier modifiers can raise it.")]
        public EnemyRank rank = EnemyRank.Minor;
        [Tooltip("Name at Major rank. Empty = Display Name with the tier prefix.")]
        public string majorName = "";
        [Tooltip("Name at Elite rank. Empty = Display Name with the tier prefix.")]
        public string eliteName = "";
        [Tooltip("Name at Miniboss rank and above. Empty = Display Name with the tier prefix.")]
        public string minibossName = "";

        [Header("Loot")]
        [Tooltip("Empty = the loot dropper's own table.")]
        public LootTableSO lootTable;

        public string StatSourceLabel => string.IsNullOrEmpty(displayName) ? name : displayName;

        public bool HasRankName(EnemyRank r) => !string.IsNullOrEmpty(RankName(r));

        // Name for a rank, falling back to the display name.
        public string NameFor(EnemyRank r)
        {
            string n = RankName(r);
            return string.IsNullOrEmpty(n) ? StatSourceLabel : n;
        }

        private string RankName(EnemyRank r) => r switch
        {
            EnemyRank.Major => majorName,
            EnemyRank.Elite => eliteName,
            EnemyRank.Miniboss or EnemyRank.Boss => minibossName,
            _ => ""
        };

        public bool DefinesBase(StatDefinitionSO stat)
        {
            for (int i = 0; i < baseStats.Count; i++)
                if (baseStats[i].stat == stat) return true;
            return false;
        }
    }
}
