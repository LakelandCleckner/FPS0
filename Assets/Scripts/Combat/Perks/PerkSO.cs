using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Combat.Loadout;

namespace Combat.Perks
{
    // When a weapon perk hears bus events. Armor perks always hear.
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

        [Header("HUD")]
        [Tooltip("Shown on the HUD tile. Empty = Short Name.")]
        public Sprite icon;
        [Tooltip("2-3 letters for the HUD tile when there's no icon. Empty = initials of the display name.")]
        public string shortName = "";

        [Tooltip("Weapon perks only. When this perk hears events from the bus.")]
        public PerkListenScope listenScope = PerkListenScope.Active;

        public abstract PerkRuntime CreateRuntime();

        // Unconditional modifiers, for stat previews of items not in the loadout. Conditional perks add nothing.
        public virtual void CollectStaticModifiers(List<ModifierEntry> into) { }

        public string ShortLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(shortName)) return shortName.Trim();
                string n = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
                var sb = new System.Text.StringBuilder(3);
                foreach (var word in n.Split(' ', '_'))
                    if (word.Length > 0 && sb.Length < 3) sb.Append(char.ToUpperInvariant(word[0]));
                return sb.ToString();
            }
        }
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
        private static readonly HashSet<string> used = new HashSet<string>();
        private static readonly HashSet<string> warned = new HashSet<string>();

        // One pick per column, uniform, never repeating a perk family already on the item (intrinsics included).
        // A column with nothing left stays empty. Curated picks are kept as authored.
        public static void Roll(List<PerkColumn> columns, List<PerkSO> curated, RollMode mode, List<PerkSO> into,
                                IEnumerable<PerkSO> intrinsic = null, string owner = null)
        {
            into.Clear();
            used.Clear();
            if (columns == null) return;

            if (intrinsic != null)
                foreach (var p in intrinsic) if (p != null) used.Add(Family(p));

            for (int i = 0; i < columns.Count; i++)
            {
                PerkSO pick = null;

                if (mode == RollMode.Curated && curated != null && i < curated.Count)
                    pick = curated[i];

                if (pick == null && columns[i] != null)
                {
                    columns[i].Gather(candidates);
                    for (int c = candidates.Count - 1; c >= 0; c--)
                        if (used.Contains(Family(candidates[c]))) candidates.RemoveAt(c);

                    if (candidates.Count > 0)
                        pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                    else
                        WarnEmpty(owner, columns[i], i);
                    candidates.Clear();
                }

                if (pick != null) used.Add(Family(pick));
                into.Add(pick);
            }
        }

        // "Sturdy III" and "Sturdy I" are one family: the name without a trailing roman numeral.
        public static string Family(PerkSO perk)
        {
            string n = (string.IsNullOrWhiteSpace(perk.displayName) ? perk.name : perk.displayName).Trim();
            int space = n.LastIndexOf(' ');
            if (space > 0 && IsRoman(n.Substring(space + 1))) n = n.Substring(0, space);
            return n.ToLowerInvariant();
        }

        private static bool IsRoman(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c != 'I' && c != 'V' && c != 'X') return false;
            return true;
        }

        private static void WarnEmpty(string owner, PerkColumn column, int index)
        {
            string label = string.IsNullOrWhiteSpace(column.label) ? "column " + (index + 1) : column.label + " (column " + (index + 1) + ")";
            string key = (owner ?? "?") + "|" + index;
            if (warned.Add(key))
                Debug.LogWarning($"[PerkRoll] {owner ?? "Item"}: {label} had no perks left after removing duplicates; left empty.");
        }
    }
}