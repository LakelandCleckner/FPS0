using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Loot;
using Combat.Stats;
using Combat.Zones;

namespace Combat.Enemies
{
    // A reusable layer stacked onto any archetype (Elite, Miniboss, Fire, ...).
    [CreateAssetMenu(fileName = "EnemyModifier", menuName = "Combat/Enemies/Enemy Modifier")]
    public class EnemyModifierSO : ScriptableObject, IStatSourceLabel
    {
        [Header("Identity")]
        public string displayName = "";
        [Tooltip("Added before the archetype name, e.g. \"Elite\".")]
        public string namePrefix = "";
        public EnemyModifierKind kind = EnemyModifierKind.Tier;
        [Tooltip("Element this layer represents, if any. Identity only.")]
        public DamageTypeSO element;

        [Header("Stats")]
        [Tooltip("Applied in order. Resists and weaknesses are stat entries on the resistance stats.")]
        public List<EnemyStatEntry> stats = new List<EnemyStatEntry>();

        [Header("Melee")]
        [Tooltip("Empty = no change. Last layer that sets one wins.")]
        public DamageTypeSO meleeDamageType;
        [Tooltip("Added to every melee move (burn, slow, poison).")]
        public List<HitEffectSO> meleeHitEffects = new List<HitEffectSO>();

        [Header("Death")]
        public List<HitEffectSO> deathEffects = new List<HitEffectSO>();

        [Header("Affixes")]
        public List<EnemyAffixSO> affixes = new List<EnemyAffixSO>();

        [Header("Visual")]
        [Tooltip("Zone colour preset. Needs FPS0/EnemyLit with a zone mask. Per zone, the last layer that sets it wins.")]
        public ZoneShaderSO look;
        [Tooltip("Added emission. Needs emission enabled on the material.")]
        [ColorUsage(false, true)] public Color emission = Color.black;
        [Tooltip("Multiplies the root scale.")]
        [Min(0.01f)] public float scale = 1f;

        [Header("Presentation")]
        [Tooltip("Raises the enemy's rank. The highest rank across layers wins.")]
        public EnemyRankRaise raiseRankTo = EnemyRankRaise.Keep;

        [Header("Loot")]
        [Tooltip("Empty = no change. Last layer that sets one wins.")]
        public LootTableSO lootTable;
        [Tooltip("The first drop ignores the table's drop chance.")]
        public bool guaranteedDrop;
        [Tooltip("Extra drop rolls on death (each uses the table's drop chance).")]
        [Min(0)] public int extraDrops;

        public string StatSourceLabel => string.IsNullOrEmpty(displayName) ? name : displayName;

        public bool SetsBase(StatDefinitionSO stat)
        {
            for (int i = 0; i < stats.Count; i++)
                if (stats[i].stat == stat && stats[i].op == EnemyStatOp.SetBase) return true;
            return false;
        }
    }
}
