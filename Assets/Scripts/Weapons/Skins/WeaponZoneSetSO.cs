using System;
using System.Collections.Generic;
using UnityEngine;

namespace Combat.Weapons
{
    // Shared zone convention: names and stable ids used by every gun mask and shader preset.
    [CreateAssetMenu(fileName = "WeaponZoneSet", menuName = "Combat/Weapons/Weapon Zone Set")]
    public class WeaponZoneSetSO : ScriptableObject
    {
        public const int MaxZones = 16;

        [Serializable]
        public class Zone
        {
            public string name = "Zone";
            [HideInInspector] public int id;
            public Color editorColor = Color.white;
        }

        public List<Zone> zones = new List<Zone>
        {
            new Zone { name = "Primary", id = 1, editorColor = new Color(1f, 0.15f, 0.15f) },
            new Zone { name = "Secondary", id = 2, editorColor = new Color(0.15f, 1f, 0.15f) },
            new Zone { name = "Metal/Accent", id = 3, editorColor = new Color(0.2f, 0.4f, 1f) },
            new Zone { name = "Glow", id = 4, editorColor = new Color(1f, 0.9f, 0.1f) }
        };

        [SerializeField, HideInInspector] private int nextId = 5;

        public Zone Find(int id)
        {
            foreach (var z in zones)
                if (z != null && z.id == id) return z;
            return null;
        }

        // New or duplicated entries get a fresh id; ids are reused only after all 16 are handed out.
        private void OnValidate()
        {
            var used = new HashSet<int>();
            var pending = new List<Zone>();
            foreach (var z in zones)
            {
                if (z == null) continue;
                if (z.id < 1 || z.id > MaxZones || !used.Add(z.id)) pending.Add(z);
            }

            foreach (var z in pending)
            {
                z.id = NextFreeId(used);
                if (z.id == 0) Debug.LogWarning($"[{name}] More than {MaxZones} zones; '{z.name}' has no id.");
                else used.Add(z.id);
            }
        }

        private int NextFreeId(HashSet<int> used)
        {
            while (nextId <= MaxZones && used.Contains(nextId)) nextId++;
            if (nextId <= MaxZones) return nextId++;

            for (int id = 1; id <= MaxZones; id++)
            {
                if (used.Contains(id)) continue;
                Debug.LogWarning($"[{name}] Reusing zone id {id}; mask pixels left from a removed zone with that id now belong to this zone.");
                return id;
            }
            return 0;
        }
    }
}
