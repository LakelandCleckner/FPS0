using System;
using System.Collections.Generic;
using UnityEngine;

namespace Combat.Perks
{
    // Timers for perks on scaled time. Disables itself when nothing is scheduled, so it costs nothing at rest.
    public class PerkClock : MonoBehaviour
    {
        private struct Timer
        {
            public long Id;
            public object Owner;
            public float At;
            public Action Callback;
        }

        private readonly List<Timer> timers = new List<Timer>();
        private readonly List<Timer> due = new List<Timer>();
        private long nextId = 1;

        // Finds the clock on the player root, adding one if missing.
        public static PerkClock For(Component c)
        {
            if (c == null) return null;
            var clock = c.GetComponentInParent<PerkClock>(true);
            if (clock != null) return clock;

            var root = c.transform.root.gameObject;
            return root.AddComponent<PerkClock>();
        }

        private void Awake()
        {
            if (timers.Count == 0) enabled = false;
        }

        public long Schedule(object owner, float delay, Action callback)
        {
            if (callback == null) return 0;

            long id = nextId++;
            timers.Add(new Timer { Id = id, Owner = owner, At = Time.time + Mathf.Max(0f, delay), Callback = callback });
            enabled = true;
            return id;
        }

        public void Cancel(long id)
        {
            if (id == 0) return;
            for (int i = timers.Count - 1; i >= 0; i--)
                if (timers[i].Id == id) { timers.RemoveAt(i); break; }
        }

        public void CancelAll(object owner)
        {
            if (owner == null) return;
            for (int i = timers.Count - 1; i >= 0; i--)
                if (ReferenceEquals(timers[i].Owner, owner)) timers.RemoveAt(i);
        }

        private void Update()
        {
            float now = Time.time;

            // Collect first so callbacks can schedule or cancel freely.
            due.Clear();
            for (int i = timers.Count - 1; i >= 0; i--)
            {
                if (timers[i].At > now) continue;
                due.Add(timers[i]);
                timers.RemoveAt(i);
            }

            for (int i = due.Count - 1; i >= 0; i--)
            {
                try { due[i].Callback(); }
                catch (Exception e) { Debug.LogError($"[PerkClock] Timer threw: {e}"); }
            }
            due.Clear();

            if (timers.Count == 0) enabled = false;
        }
    }
}
