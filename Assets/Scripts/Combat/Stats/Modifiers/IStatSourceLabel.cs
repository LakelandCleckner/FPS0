namespace Combat.Stats
{
    // A modifier owner that can name itself in stat breakdowns.
    public interface IStatSourceLabel
    {
        string StatSourceLabel { get; }
    }

    public static class StatSource
    {
        // The owner's label, else its type name.
        public static string Label(object owner)
        {
            if (owner == null) return "Unknown";
            if (owner is IStatSourceLabel l)
            {
                string s = l.StatSourceLabel;
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return owner.GetType().Name;
        }
    }

    // One modifier on a stat, as the breakdown reports it.
    public struct StatBreakdownLine
    {
        public string BucketId;
        public float Value;        // derived ones already resolved
        public object Owner;
        public bool IsDerived;
        public StatDefinitionSO SourceStat;
    }
}
