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

    // Item rarity. Sets which columns roll and which perk tier each column takes.
    public enum Rarity { Common, Rare, Legendary, Mythic }

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
        [Tooltip("Rolls only on items of this rarity or higher; lower rarities leave it empty.")]
        public Rarity minRarity = Rarity.Common;
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

        private static readonly Dictionary<string, List<PerkSO>> families = new Dictionary<string, List<PerkSO>>();
        private static readonly List<string> familyKeys = new List<string>();

        // One pick per column: a random family not already on the item (intrinsics included), at the tier the rarity allows.
        // Columns above the item's rarity, or with nothing left, stay empty. Curated picks are kept as authored.
        public static void Roll(List<PerkColumn> columns, List<PerkSO> curated, RollMode mode, List<PerkSO> into,
                                IEnumerable<PerkSO> intrinsic = null, string owner = null, Rarity rarity = Rarity.Common)
        {
            into.Clear();
            used.Clear();
            if (columns == null) return;

            if (intrinsic != null)
                foreach (var p in intrinsic) if (p != null) used.Add(Family(p));

            for (int i = 0; i < columns.Count; i++)
            {
                PerkSO pick = null;

                if (columns[i] != null && columns[i].minRarity > rarity) { into.Add(null); continue; }

                if (mode == RollMode.Curated && curated != null && i < curated.Count)
                    pick = curated[i];

                if (pick == null && columns[i] != null)
                {
                    columns[i].Gather(candidates);
                    pick = PickFamily(candidates, rarity);
                    if (pick == null) WarnEmpty(owner, columns[i], i);
                    candidates.Clear();
                }

                if (pick != null) used.Add(Family(pick));
                into.Add(pick);
            }
        }

        // A random unused family, then its highest tier at or below the rarity (Common I, Rare II, Legendary III, Mythic IV).
        // A family with no tier that low gives its lowest.
        private static PerkSO PickFamily(List<PerkSO> perks, Rarity rarity)
        {
            families.Clear();
            familyKeys.Clear();
            foreach (var p in perks)
            {
                if (p == null) continue;
                string f = Family(p);
                if (used.Contains(f)) continue;
                if (!families.TryGetValue(f, out var list)) { families[f] = list = new List<PerkSO>(); familyKeys.Add(f); }
                if (!list.Contains(p)) list.Add(p);
            }
            if (familyKeys.Count == 0) return null;

            var tiers = families[familyKeys[UnityEngine.Random.Range(0, familyKeys.Count)]];
            int target = (int)rarity + 1;
            PerkSO best = null, lowest = null;
            foreach (var p in tiers)
            {
                int t = Tier(p);
                if (t <= target && (best == null || t > Tier(best))) best = p;
                if (lowest == null || t < Tier(lowest)) lowest = p;
            }
            return best ?? lowest;
        }

        // "Sturdy III" and "Sturdy I" are one family: the name without a trailing roman numeral.
        public static string Family(PerkSO perk)
        {
            string n = Name(perk);
            int space = n.LastIndexOf(' ');
            if (space > 0 && Roman(n.Substring(space + 1)) > 0) n = n.Substring(0, space);
            return n.ToLowerInvariant();
        }

        // The trailing roman numeral, or 1 for a perk without one.
        public static int Tier(PerkSO perk)
        {
            string n = Name(perk);
            int space = n.LastIndexOf(' ');
            int t = space > 0 ? Roman(n.Substring(space + 1)) : 0;
            return t > 0 ? t : 1;
        }

        private static string Name(PerkSO perk) => (string.IsNullOrWhiteSpace(perk.displayName) ? perk.name : perk.displayName).Trim();

        // I..XXXIX, 0 if not a numeral.
        private static int Roman(string s)
        {
            if (s.Length == 0) return 0;
            int total = 0, prev = 0;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                int v = s[i] == 'I' ? 1 : s[i] == 'V' ? 5 : s[i] == 'X' ? 10 : 0;
                if (v == 0) return 0;
                total += v < prev ? -v : v;
                prev = Mathf.Max(prev, v);
            }
            return total;
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