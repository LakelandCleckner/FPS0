using System.Collections.Generic;
using UnityEngine;

namespace Combat.AI
{
    // Limits how many enemies attack one target at once. A holder keeps its token while engaged and
    // gives it back when it dies, despawns, loses the target or breaks off (e.g. to take cover).
    public static class AttackTokens
    {
        public const int DefaultMeleeBudget = 5;

        private class Pool
        {
            public int Budget = DefaultMeleeBudget;
            public readonly HashSet<object> Holders = new HashSet<object>();
        }

        private static readonly Dictionary<Transform, Pool> pools = new Dictionary<Transform, Pool>();

        public static void SetBudget(Transform target, int budget)
        {
            if (target != null) Get(target).Budget = Mathf.Max(0, budget);
        }

        public static bool Holds(Transform target, object holder)
            => target != null && pools.TryGetValue(target, out var p) && p.Holders.Contains(holder);

        public static bool TryAcquire(Transform target, object holder)
        {
            if (target == null || holder == null) return false;
            var p = Get(target);
            if (p.Holders.Contains(holder)) return true;
            if (p.Holders.Count >= p.Budget) return false;
            p.Holders.Add(holder);
            return true;
        }

        public static void Release(Transform target, object holder)
        {
            if (target != null && pools.TryGetValue(target, out var p)) p.Holders.Remove(holder);
        }

        // Drops every token this holder has, on any target.
        public static void ReleaseAll(object holder)
        {
            foreach (var p in pools.Values) p.Holders.Remove(holder);
        }

        public static int InUse(Transform target) => target != null && pools.TryGetValue(target, out var p) ? p.Holders.Count : 0;

        private static Pool Get(Transform target)
        {
            if (!pools.TryGetValue(target, out var p)) pools[target] = p = new Pool();
            return p;
        }
    }
}
