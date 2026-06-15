using UnityEngine;

namespace Combat.Feedback
{
    // Formats a damage value for display following the project's readability
    // ladder:
    //   under 100        -> 1 decimal place        e.g. "27.5"
    //   100 .. 999,999   -> integer with commas    e.g. "1,450", "999,999"
    //   1,000,000+       -> letter abbreviation starting at 'm', lowercase, with
    //                       ~4 significant figures: decimals step down as the
    //                       leading digits grow and reset each tier:
    //                         1.450m / 10.45m / 100.4m / 1.450b / 10.45b / 100.4b
    //                       (no 'k' tier by design — full numbers up to 999,999)
    //
    // m/b/t implemented now; extend the suffix table for higher tiers later.
    public static class DamageNumberFormatter
    {
        // suffix table; index 0 = millions
        private static readonly string[] suffixes = { "m", "b", "t" };
        private static readonly double[] thresholds =
        {
            1_000_000d,         // m
            1_000_000_000d,     // b
            1_000_000_000_000d  // t
        };

        public static string Format(float amount)
        {
            // small values keep the 1-decimal rule
            if (amount < 100f)
                return amount.ToString("0.0");

            // mid values: full integer with thousands separators, no abbreviation
            if (amount < 1_000_000f)
                return Mathf.Round(amount).ToString("#,0");

            // large values: abbreviate from millions up, lowercase letter,
            // ~4 significant figures (decimals reset per tier, step 3->2->1)
            double value = amount;
            for (int i = suffixes.Length - 1; i >= 0; i--)
            {
                if (value >= thresholds[i])
                {
                    double scaled = value / thresholds[i];
                    return scaled.ToString(DecimalFormatFor(scaled)) + suffixes[i];
                }
            }

            // fallback (shouldn't hit, but safe)
            return Mathf.Round(amount).ToString("#,0");
        }

        // Keeps ~4 significant figures within a tier: 1-9.999 -> 3 decimals,
        // 10-99.99 -> 2 decimals, 100+ -> 1 decimal.
        private static string DecimalFormatFor(double scaled)
        {
            if (scaled < 10d) return "0.000";
            if (scaled < 100d) return "0.00";
            return "0.0";
        }
    }
}