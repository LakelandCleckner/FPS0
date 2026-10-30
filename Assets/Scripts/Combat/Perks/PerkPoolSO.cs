using System.Collections.Generic;
using UnityEngine;

namespace Combat.Perks
{
    // A reusable list of perks for roll columns. Can include other pools.
    [CreateAssetMenu(fileName = "PerkPool", menuName = "Combat/Perks/Perk Pool")]
    public class PerkPoolSO : ScriptableObject
    {
        public List<PerkSO> perks = new List<PerkSO>();
        [Tooltip("Every perk in these pools is part of this one too.")]
        public List<PerkPoolSO> includes = new List<PerkPoolSO>();

        [Tooltip("The Perk Pool Batch line this pool was built from, so folder rules survive Load Current.")]
        [TextArea] public string authoredAs = "";

        // Adds this pool's perks and its includes' perks, once each. Include loops are skipped.
        public void Collect(List<PerkSO> into, HashSet<PerkPoolSO> visited = null)
        {
            if (visited == null) visited = new HashSet<PerkPoolSO>();
            if (!visited.Add(this)) return;

            foreach (var p in perks)
                if (p != null && !into.Contains(p)) into.Add(p);

            foreach (var inc in includes)
                if (inc != null) inc.Collect(into, visited);
        }
    }
}