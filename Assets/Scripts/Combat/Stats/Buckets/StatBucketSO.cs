using System.Collections.Generic;
using UnityEngine;

namespace Combat.Stats
{
    // How a bucket combines its contents.
    public enum BucketRule
    {
        Sum,        // children/modifiers ADD; contributes (1 + sum) as a factor
        Multiply    // children each contribute a factor; they MULTIPLY together
    }

    // A node in a stat's bucket TREE (GDD 14 §5). A bucket either:
    //   - is a LEAF that collects modifiers tagged with its id, or
    //   - is a BRANCH that combines child buckets.
    // Combine rule is Sum or Multiply — the only two rules; subtraction is a
    // negative value, division is a sub-1 factor (sign/magnitude express direction).
    //
    // Authorable + nestable: a StatBucketSO can reference child StatBucketSO assets,
    // forming arbitrary trees. Most stats use a shared DEFAULT tree (see
    // StatResolver.DefaultTree); a stat only needs a custom tree for special
    // grouping (e.g. crit_damage's inherent+additive pool vs multiplicative sources).
    [CreateAssetMenu(fileName = "StatBucket", menuName = "Combat/Stats/Stat Bucket")]
    public class StatBucketSO : ScriptableObject
    {
        [Tooltip("Identity a modifier targets when it wants to feed THIS bucket.")]
        public string bucketId = "";

        [Tooltip("How this bucket combines its contents.")]
        public BucketRule rule = BucketRule.Sum;

        [Tooltip("Child buckets (branch). If empty, this is a leaf that collects " +
                 "modifiers tagged with its bucketId.")]
        public List<StatBucketSO> children = new List<StatBucketSO>();

        public bool IsLeaf => children == null || children.Count == 0;
    }
}
