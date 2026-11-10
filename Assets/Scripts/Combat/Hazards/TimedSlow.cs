using System.Collections.Generic;
using UnityEngine;
using Combat.Stats;

namespace Combat.Hazards
{
    // Short slows that lift themselves. Re-applying from the same source refreshes the timer.
    public class TimedSlow : MonoBehaviour
    {
        private struct Entry
        {
            public StatContainer Container;
            public object Source;
            public float Until;
        }

        private static TimedSlow runner;
        private readonly List<Entry> entries = new List<Entry>();

        // amount 0.4 = 40% slower on stat (move_speed) for duration seconds.
        public static void Apply(StatContainer c, StatDefinitionSO stat, float amount, float duration, object source)
        {
            if (c == null || stat == null || amount <= 0f || duration <= 0f || source == null) return;
            if (runner == null) runner = new GameObject("TimedSlow").AddComponent<TimedSlow>();

            float until = Time.time + duration;
            var list = runner.entries;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Container != c || list[i].Source != source) continue;
                var e = list[i];
                e.Until = Mathf.Max(e.Until, until);
                list[i] = e;
                return;
            }

            c.AddModifier(new StatModifier(stat, StatResolver.MULTIPLICATIVE, -Mathf.Min(amount, 0.95f)), source);
            list.Add(new Entry { Container = c, Source = source, Until = until });
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Until > now) continue;
                entries[i].Container.RemoveAllFromOwner(entries[i].Source);
                entries.RemoveAt(i);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < entries.Count; i++) entries[i].Container.RemoveAllFromOwner(entries[i].Source);
            entries.Clear();
            if (runner == this) runner = null;
        }
    }
}
