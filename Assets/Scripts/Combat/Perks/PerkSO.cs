using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Combat.Perks
{
    // When a weapon perk hears bus events. Armour perks always hear.
    public enum PerkListenScope
    {
        Active,     // only while its weapon is in hand
        Always,     // while its weapon is in the loadout, held or stowed
        StowedOnly  // only while its weapon is stowed
    }

    public enum RollMode { Random, Curated }

    // A perk recipe: parameters and wiring. CreateRuntime builds the per-item instance that holds state.
    public abstract class PerkSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves. Set once, don't rename.")]
        public string id = "";
        public string displayName = "";
        [TextArea] public string description = "";

        [Tooltip("Weapon perks only. When this perk hears events from the bus.")]
        public PerkListenScope listenScope = PerkListenScope.Active;

        public abstract PerkRuntime CreateRuntime();
    }

    // One roll column: a shared pool plus optional one-off perks, one picked per roll.
    [Serializable]
    public class PerkColumn
    {
        public string label = "";
        [Tooltip("Shared pool. Editing it changes every column that uses it.")]
        public PerkPoolSO source;
        [Tooltip("Optional. Perks for this column only, on top of the source pool. Usually empty.")]
        [FormerlySerializedAs("pool")]
        public List<PerkSO> extraPerks = new List<PerkSO>();

        // Source pool plus extras, once each.
        public void Gather(List<PerkSO> into)
        {
            into.Clear();
            if (source != null) source.Collect(into);
            foreach (var p in extraPerks)
                if (p != null && !into.Contains(p)) into.Add(p);
        }
    }

    public static class PerkRoll
    {
        private static readonly List<PerkSO> candidates = new List<PerkSO>();

        // One pick per column, uniform. Curated uses the matching curated pick when set.
        public static void Roll(List<PerkColumn> columns, List<PerkSO> curated, RollMode mode, List<PerkSO> into)
        {
            into.Clear();
            if (columns == null) return;

            for (int i = 0; i < columns.Count; i++)
            {
                PerkSO pick = null;

                if (mode == RollMode.Curated && curated != null && i < curated.Count)
                    pick = curated[i];

                if (pick == null && columns[i] != null)
                {
                    columns[i].Gather(candidates);
                    if (candidates.Count > 0)
                        pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                    candidates.Clear();
                }

                into.Add(pick);
            }
        }
    }
}