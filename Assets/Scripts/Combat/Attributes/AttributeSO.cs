using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Loadout;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Attributes
{
    // One stat an attribute feeds, per point, on the player or every loadout weapon.
    [Serializable]
    public class AttributeFeed
    {
        public StatDefinitionSO stat;

        [Tooltip("Flat adds to the base. Additive: percent on ScaledBase stats (0.01 = +1%), " +
                 "a straight add on Pool stats. Multiplicative: its own x(1 + value).")]
        public ModifierBucket bucket = ModifierBucket.Additive;

        [Tooltip("Contribution per attribute point.")]
        public float perPoint;

        [Tooltip("Player stats, or the stats of every loadout weapon.")]
        public ModifierTarget target = ModifierTarget.Player;
    }

    // An attribute: its stat, the element and archetypes it's tied to, and the stats it feeds.
    [CreateAssetMenu(fileName = "Attribute", menuName = "Combat/Attributes/Attribute")]
    public class AttributeSO : ScriptableObject, IStatSourceLabel
    {
        [Tooltip("The attribute's own player stat. Allocated points are its base; gear and talents add on top.")]
        public StatDefinitionSO stat;

        [TextArea(1, 3)] public string theme = "";

        [Header("Links")]
        public DamageTypeSO element;
        public List<WeaponArchetypeSO> archetypes = new List<WeaponArchetypeSO>();

        [Header("Feeds")]
        public List<AttributeFeed> feeds = new List<AttributeFeed>();

        public string DisplayName =>
            stat != null && !string.IsNullOrWhiteSpace(stat.displayName) ? stat.displayName : name;

        // Breakdowns read "Attribute  (from Might)".
        public string StatSourceLabel => "Attribute";
    }
}
