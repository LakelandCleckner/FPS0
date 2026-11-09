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

    // Enemy rank: drives the health bar colour, rank icon and name display. Boss never uses the overhead bar.
    public enum EnemyRank { Minor, Major, Elite, Miniboss, Boss }

    // A modifier's rank change; the highest rank across layers wins.
    public enum EnemyRankRaise { Keep = 0, Major = 1, Elite = 2, Miniboss = 3, Boss = 4 }

    // Optional shield on an enemy, drawn over its health bar.
    public interface IShieldSource
    {
        float CurrentShield { get; }
        float MaxShield { get; }
        UnityEngine.Color ShieldColor { get; }
    }

    public enum EnemyModifierKind { Tier, Element, Other }
}
