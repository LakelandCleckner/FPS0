using System.Collections.Generic;
using UnityEngine;

namespace Combat.AI
{
    public enum AttackKind { Melee, Ranged }

    // Limits how many enemies attack one target at once, per attack kind. A holder keeps its token while engaged and
    // gives it back when it dies, despawns, loses the target or breaks off (e.g. to take cover).
    public static class AttackTokens
    {
        public const int Unlimited = -1;
        public const int DefaultMeleeBudget = 5;
        public const int DefaultRangedBudget = Unlimited;

        private class Pool
        {
            public int Budget;
            public readonly HashSet<object> Holders = new HashSet<object>();
        }

        private static readonly Dictionary<(Transform, AttackKind), Pool> pools = new Dictionary<(Transform, AttackKind), Pool>();

        public static void SetBudget(Transform target, int budget) => SetBudget(target, AttackKind.Melee, budget);

        // Negative = unlimited
        public static void SetBudget(Transform target, AttackKind kind, int budget)
        {
            if (target != null) Get(target, kind).Budget = budget < 0 ? Unlimited : budget;
        }

        public static int Budget(Transform target, AttackKind kind)
            => target != null ? Get(target, kind).Budget : Default(kind);

        public static bool Holds(Transform target, object holder) => Holds(target, holder, AttackKind.Melee);

        public static bool Holds(Transform target, object holder, AttackKind kind)
            => target != null && pools.TryGetValue((target, kind), out var p) && p.Holders.Contains(holder);

        public static bool TryAcquire(Transform target, object holder) => TryAcquire(target, holder, AttackKind.Melee);

        public static bool TryAcquire(Transform target, object holder, AttackKind kind)
        {
            if (target == null || holder == null) return false;
            var p = Get(target, kind);
            if (p.Holders.Contains(holder)) return true;
            if (p.Budget != Unlimited && p.Holders.Count >= p.Budget) return false;
            p.Holders.Add(holder);
            return true;
        }

        public static void Release(Transform target, object holder) => Release(target, holder, AttackKind.Melee);

        public static void Release(Transform target, object holder, AttackKind kind)
        {
            if (target != null && pools.TryGetValue((target, kind), out var p)) p.Holders.Remove(holder);
        }

        // Drops every token this holder has, on any target and kind.
        public static void ReleaseAll(object holder)
        {
            foreach (var p in pools.Values) p.Holders.Remove(holder);
        }

        public static int InUse(Transform target) => InUse(target, AttackKind.Melee);

        public static int InUse(Transform target, AttackKind kind)
            => target != null && pools.TryGetValue((target, kind), out var p) ? p.Holders.Count : 0;

        private static int Default(AttackKind kind) => kind == AttackKind.Melee ? DefaultMeleeBudget : DefaultRangedBudget;

        private static Pool Get(Transform target, AttackKind kind)
        {
            if (!pools.TryGetValue((target, kind), out var p))
                pools[(target, kind)] = p = new Pool { Budget = Default(kind) };
            return p;
        }
    }
}
