using System.Collections.Generic;
using UnityEngine;

namespace Combat.Core
{
    // Central list of all DamageTypeSO assets, for systems that need to reach a
    // type WITHOUT a serialized reference — save/load by id, UI listing,
    // type-interaction lookups, debug commands. Decoupling tool, not a perf win.
    //
    // Maintenance: new DamageTypeSO assets must be added to 'types' or lookups
    // miss them. (Could auto-populate via an editor scan later.)
    [CreateAssetMenu(fileName = "DamageTypeRegistry", menuName = "Combat/Damage Type Registry")]
    public class DamageTypeRegistry : ScriptableObject
    {
        [SerializeField] private List<DamageTypeSO> types = new List<DamageTypeSO>();

        private Dictionary<string, DamageTypeSO> lookup;

        private void BuildLookup()
        {
            lookup = new Dictionary<string, DamageTypeSO>();
            foreach (var t in types)
                if (t != null && !lookup.ContainsKey(t.id))
                    lookup.Add(t.id, t);
        }

        // Lookup by stable id.
        public DamageTypeSO Get(string id)
        {
            if (lookup == null) BuildLookup();
            return lookup.TryGetValue(id, out var t) ? t : null;
        }

        public IReadOnlyList<DamageTypeSO> All => types;
    }
}
