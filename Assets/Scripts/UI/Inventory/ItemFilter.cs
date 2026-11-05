using System;
using System.Collections.Generic;
using Combat.Armor;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.UI
{
    public enum ItemKindFilter { All, Weapons, Armor }
    public enum ItemSort { Rarity, Name, Type, Newest, Perks }

    // Filters and sort for the vault tab grids. An empty set means that filter is off.
    public sealed class ItemFilter
    {
        public ItemKindFilter Kind = ItemKindFilter.All;
        public readonly HashSet<Rarity> Rarities = new HashSet<Rarity>();
        public readonly HashSet<WeaponTypeSO> WeaponTypes = new HashSet<WeaponTypeSO>();
        public readonly HashSet<int> WeaponSlots = new HashSet<int>();
        public readonly HashSet<string> Elements = new HashSet<string>();
        public readonly HashSet<ArmorSlotSO> ArmorSlots = new HashSet<ArmorSlotSO>();
        public string Search = "";
        public ItemSort Sort = ItemSort.Rarity;
        public bool Descending = true;

        private readonly List<(object item, int order)> scratch = new List<(object, int)>();

        public bool IsDefault => Kind == ItemKindFilter.All && Rarities.Count == 0 && WeaponTypes.Count == 0 && WeaponSlots.Count == 0
                                 && Elements.Count == 0 && ArmorSlots.Count == 0 && string.IsNullOrWhiteSpace(Search);

        public void Clear()
        {
            Kind = ItemKindFilter.All;
            Rarities.Clear();
            WeaponTypes.Clear();
            WeaponSlots.Clear();
            Elements.Clear();
            ArmorSlots.Clear();
            Search = "";
        }

        public static void Toggle<T>(HashSet<T> set, T value)
        {
            if (!set.Remove(value)) set.Add(value);
        }

        public void NextSort() => Sort = (ItemSort)(((int)Sort + 1) % Enum.GetValues(typeof(ItemSort)).Length);

        // Weapon-only chips imply weapons and armor-slot chips imply armor; both together show both.
        private bool WeaponChips => WeaponTypes.Count > 0 || WeaponSlots.Count > 0 || Elements.Count > 0;
        private bool ArmorChips => ArmorSlots.Count > 0;

        public bool Matches(object item)
        {
            var r = ItemText.RarityOf(item);
            if (Rarities.Count > 0 && (!r.HasValue || !Rarities.Contains(r.Value))) return false;

            if (item is WeaponInstance w && w.Definition != null)
            {
                if (Kind == ItemKindFilter.Armor || (!WeaponChips && ArmorChips)) return false;
                if (WeaponTypes.Count > 0 && !WeaponTypes.Contains(TypeOf(w))) return false;
                if (WeaponSlots.Count > 0 && !WeaponSlots.Contains(w.Definition.InventorySlot)) return false;
                if (Elements.Count > 0 && !Elements.Contains(ItemText.Element(w))) return false;
            }
            else if (item is ArmorInstance a && a.Definition != null)
            {
                if (Kind == ItemKindFilter.Weapons || (WeaponChips && !ArmorChips)) return false;
                if (ArmorSlots.Count > 0 && !ArmorSlots.Contains(a.Slot)) return false;
            }
            else return false;

            return string.IsNullOrWhiteSpace(Search) || SearchHit(item, Search.Trim());
        }

        // Matching items from both lists, sorted. Order is each item's position in its list (later = newer).
        public void Apply(IReadOnlyList<WeaponInstance> weapons, IReadOnlyList<ArmorInstance> armor, IList<ArmorSlotSO> slotOrder, List<object> into)
        {
            scratch.Clear();
            if (weapons != null) for (int i = 0; i < weapons.Count; i++) if (Matches(weapons[i])) scratch.Add((weapons[i], i));
            if (armor != null) for (int i = 0; i < armor.Count; i++) if (Matches(armor[i])) scratch.Add((armor[i], i));

            scratch.Sort((x, y) =>
            {
                int c = Primary(x, y, slotOrder);
                if (Descending) c = -c;
                if (c != 0) return c;
                c = -RarityRank(x.item).CompareTo(RarityRank(y.item));
                if (c != 0) return c;
                c = string.Compare(ItemText.Name(x.item), ItemText.Name(y.item), StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : x.order.CompareTo(y.order);
            });

            into.Clear();
            foreach (var s in scratch) into.Add(s.item);
            scratch.Clear();
        }

        private int Primary((object item, int order) x, (object item, int order) y, IList<ArmorSlotSO> slotOrder)
        {
            switch (Sort)
            {
                case ItemSort.Rarity: return RarityRank(x.item).CompareTo(RarityRank(y.item));
                case ItemSort.Name: return -string.Compare(ItemText.Name(x.item), ItemText.Name(y.item), StringComparison.OrdinalIgnoreCase);
                case ItemSort.Type: return -TypeRank(x.item, slotOrder).CompareTo(TypeRank(y.item, slotOrder));
                case ItemSort.Newest: return x.order.CompareTo(y.order);
                case ItemSort.Perks: return PerkCount(x.item).CompareTo(PerkCount(y.item));
                default: return 0;
            }
        }

        public static string SortLabel(ItemSort s) => s == ItemSort.Perks ? "Perk count" : s.ToString();

        // Descending is each sort's natural order (high rarity, A-Z, slot 1 first, newest, most perks).
        public static string DirectionLabel(ItemSort s, bool descending)
        {
            switch (s)
            {
                case ItemSort.Rarity: return descending ? "High first" : "Low first";
                case ItemSort.Name: return descending ? "A-Z" : "Z-A";
                case ItemSort.Type: return descending ? "Weapons first" : "Armor first";
                case ItemSort.Newest: return descending ? "Newest first" : "Oldest first";
                default: return descending ? "Most first" : "Fewest first";
            }
        }

        public static WeaponTypeSO TypeOf(WeaponInstance w)
            => w != null && w.Definition != null && w.Definition.archetype != null ? w.Definition.archetype.weaponType : null;

        private static int RarityRank(object item)
        {
            var r = ItemText.RarityOf(item);
            return r.HasValue ? (int)r.Value : -1;
        }

        // Weapons by carried slot, then armor by slot order.
        private static int TypeRank(object item, IList<ArmorSlotSO> slotOrder)
        {
            if (item is WeaponInstance w && w.Definition != null) return w.Definition.InventorySlot;
            if (item is ArmorInstance a) return 100 + (slotOrder != null ? slotOrder.IndexOf(a.Slot) : 0);
            return 1000;
        }

        private static int PerkCount(object item)
        {
            IReadOnlyList<PerkSO> perks = item is WeaponInstance w ? w.Perks : item is ArmorInstance a ? a.Perks : null;
            int n = 0;
            if (perks != null) foreach (var p in perks) if (p != null) n++;
            return n;
        }

        // Name, kind line, rarity or any perk name.
        private static bool SearchHit(object item, string q)
        {
            if (Has(ItemText.Name(item), q) || Has(ItemText.Kind(item), q)) return true;
            var r = ItemText.RarityOf(item);
            if (r.HasValue && Has(r.Value.ToString(), q)) return true;

            IEnumerable<PerkSO> perks = item is WeaponInstance w ? w.AllPerks() : item is ArmorInstance a ? a.AllPerks() : null;
            if (perks != null) foreach (var p in perks) if (Has(ItemText.PerkName(p), q)) return true;
            return false;
        }

        private static bool Has(string s, string q) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
