using System.Collections.Generic;
using UnityEngine;

namespace Combat.Stats
{
    // Resolves a stat's final value (GDD 14 §5). Two default shapes, chosen by the
    // stat's StatResolutionMode:
    //
    //   ScaledBase: (base + flat) × (1 + Σ additive) × Π(mult)
    //               base is a real quantity the buckets scale (weapon_damage, rpm).
    //   Pool:       (base + flat + Σ additive) × Π(mult)
    //               base is the inherent contribution IN the additive pool
    //               (crit_damage's +50%, crit_chance, global_damage). D4 crit model.
    //
    // Custom StatBucketSO trees override the default shape via a generic fold.
    //
    // Two combine rules only (Sum, Multiply); negatives subtract, sub-1 factors
    // divide. Each (1+Σ) floored at 0. Stat min/max clamp applied last.
    //
    // Phase 2b: pure math over base + modifier list. Caching/sources in 2c.
    public static class StatResolver
    {
        public const string FLAT = "flat";
        public const string ADDITIVE = "additive";
        public const string MULTIPLICATIVE = "mult";

        public static float Resolve(
            StatDefinitionSO stat,
            float baseValue,
            IReadOnlyList<StatModifierValue> modifiers,
            StatBucketSO tree = null)
        {
            float result;

            if (tree == null)
            {
                float flatSum = SumBucket(modifiers, FLAT);
                float additiveSum = SumBucket(modifiers, ADDITIVE);
                float multProduct = MultProduct(modifiers);

                var mode = stat != null ? stat.resolutionMode : StatResolutionMode.ScaledBase;
                if (mode == StatResolutionMode.Pool)
                {
                    // base is the inherent, living IN the additive pool
                    result = Mathf.Max(0f, baseValue + flatSum + additiveSum) * multProduct;
                }
                else // ScaledBase
                {
                    result = (baseValue + flatSum) * Factor(additiveSum) * multProduct;
                }
            }
            else
            {
                // custom tree: base + flat, scaled by the folded tree factor
                float flatSum = SumBucket(modifiers, FLAT);
                float treeFactor = FoldNode(tree, modifiers);
                result = (baseValue + flatSum) * treeFactor;
            }

            if (stat != null) result = stat.Clamp(result);
            return result;
        }

        private static float SumBucket(IReadOnlyList<StatModifierValue> modifiers, string bucketId)
        {
            if (modifiers == null) return 0f;
            float s = 0f;
            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i].BucketId == bucketId)
                    s += modifiers[i].Value;
            return s;
        }

        // Π of (1 + each multiplicative modifier), each floored at 0.
        private static float MultProduct(IReadOnlyList<StatModifierValue> modifiers)
        {
            float f = 1f;
            if (modifiers != null)
                for (int i = 0; i < modifiers.Count; i++)
                    if (modifiers[i].BucketId == MULTIPLICATIVE)
                        f *= Factor(modifiers[i].Value);
            return f;
        }

        // (1 + sum), floored at 0 so negatives can't flip the product sign.
        private static float Factor(float sum) => Mathf.Max(0f, 1f + sum);

        // Recursive fold for custom trees.
        private static float FoldNode(StatBucketSO node, IReadOnlyList<StatModifierValue> modifiers)
        {
            if (node == null) return 1f;

            if (node.IsLeaf)
            {
                if (node.rule == BucketRule.Sum)
                    return Factor(SumBucket(modifiers, node.bucketId));

                float f = 1f;
                if (modifiers != null)
                    for (int i = 0; i < modifiers.Count; i++)
                        if (modifiers[i].BucketId == node.bucketId)
                            f *= Factor(modifiers[i].Value);
                return f;
            }

            if (node.rule == BucketRule.Multiply)
            {
                float f = 1f;
                for (int i = 0; i < node.children.Count; i++)
                    f *= FoldNode(node.children[i], modifiers);
                return f;
            }

            float sum = 0f;
            for (int i = 0; i < node.children.Count; i++)
                sum += (FoldNode(node.children[i], modifiers) - 1f);
            return Factor(sum);
        }
    }
}