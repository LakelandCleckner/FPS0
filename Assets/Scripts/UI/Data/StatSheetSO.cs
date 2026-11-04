using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Stats;

namespace Combat.UI
{
    public enum StatDisplay { Bar, Number }

    // Which stats a UI block shows, in what order, and how.
    [CreateAssetMenu(fileName = "StatSheet", menuName = "Combat/UI/Stat Sheet")]
    public class StatSheetSO : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public StatDefinitionSO stat;
            [Tooltip("Empty = the stat's display name.")]
            public string label = "";
            public StatDisplay display = StatDisplay.Bar;
            [Tooltip("Bar: the value that fills the bar.")]
            [Min(0.01f)] public float barMax = 100f;
            [Tooltip("Colours a lower value as the better one (reload time, equip time).")]
            public bool lowerIsBetter = false;
            [Tooltip("Shown after the number, e.g. s or x.")]
            public string unit = "";
            [Tooltip("Decimal places. -1 = the stat's own display decimals.")]
            [Range(-1, 4)] public int decimals = -1;

            public string Label
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(label)) return label;
                    if (stat == null) return "?";
                    return string.IsNullOrWhiteSpace(stat.displayName) ? stat.name : stat.displayName;
                }
            }
        }

        [Serializable]
        public class Group
        {
            [Tooltip("Empty = no group heading.")]
            public string label = "";
            public List<Entry> entries = new List<Entry>();
        }

        public List<Group> groups = new List<Group>();
    }

    // Number formatting from a stat's display hints.
    public static class StatFormat
    {
        public static string Value(StatDefinitionSO stat, float v, string unit = "", int decimals = -1)
        {
            if (stat != null && stat.isPercent) return (v * 100f).ToString("0.#") + "%";
            int d = decimals >= 0 ? decimals : stat != null ? Mathf.Clamp(stat.displayDecimals, 0, 4) : 0;
            return v.ToString("F" + d) + unit;
        }

        public static string Delta(StatDefinitionSO stat, float delta, string unit = "", int decimals = -1)
        {
            if (Mathf.Abs(delta) < 0.0005f) return "";
            return (delta > 0f ? "+" : "-") + Value(stat, Mathf.Abs(delta), unit, decimals);
        }

        public static string Value(StatSheetSO.Entry e, float v) => Value(e.stat, v, e.unit, e.decimals);
        public static string Delta(StatSheetSO.Entry e, float delta) => Delta(e.stat, delta, e.unit, e.decimals);
    }
}
