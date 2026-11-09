using System;
using UnityEngine;
using Combat.Stats;

namespace Combat.Enemies
{
    public enum EnemyStatOp
    {
        Flat,       // added to base
        Additive,   // ScaledBase: summed % bonus (0.5 = +50%); Pool: added to the pool
        Multiply,   // factor (1.5 = x1.5), stacks multiplicatively
        SetBase     // replaces the base value
    }

    // One stat change from a modifier layer.
    [Serializable]
    public struct EnemyStatEntry
    {
        public StatDefinitionSO stat;
        public EnemyStatOp op;
        [Tooltip("Flat/SetBase: raw value. Additive: 0.5 = +50%. Multiply: factor, 1.5 = x1.5.")]
        public float value;

        // Adds this entry as a container modifier; SetBase is handled by the caller.
        public ModifierHandle AddTo(StatContainer c, object owner)
        {
            if (c == null || stat == null || op == EnemyStatOp.SetBase) return ModifierHandle.None;
            switch (op)
            {
                case EnemyStatOp.Flat: return c.AddModifier(new StatModifier(stat, StatResolver.FLAT, value), owner);
                case EnemyStatOp.Additive: return c.AddModifier(new StatModifier(stat, StatResolver.ADDITIVE, value), owner);
                default: return c.AddModifier(new StatModifier(stat, StatResolver.MULTIPLICATIVE, value - 1f), owner);
            }
        }
    }

    // Which health bar an enemy shows. Higher wins when layers combine.
    public enum EnemyHealthBarMode { None, Overhead, Boss }

    public enum EnemyModifierKind { Tier, Element, Other }
}
