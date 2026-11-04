using System.Collections.Generic;
using Combat.Armor;
using Combat.Loadout;
using Combat.Weapons;

namespace Combat.UI
{
    public enum SlotKind { Weapon, Armor }

    public struct SlotRef
    {
        public SlotKind Kind;
        public int Index;

        public SlotRef(SlotKind kind, int index) { Kind = kind; Index = index; }
        public bool Is(SlotKind kind, int index) => Kind == kind && Index == index;
    }

    // What the inventory screen draws: a copy of the live loadout, or a preview built from a starting loadout.
    public sealed class LoadoutView
    {
        public readonly WeaponInstance[] Weapons = new WeaponInstance[LoadoutSlots.Count];
        public readonly List<ArmorSlotSO> ArmorSlots = new List<ArmorSlotSO>();
        public readonly List<ArmorInstance> Armor = new List<ArmorInstance>();
        public readonly List<WeaponInstance> CarriedWeapons = new List<WeaponInstance>();
        public readonly List<ArmorInstance> CarriedArmor = new List<ArmorInstance>();
        public int WeaponCapPerSlot = 9;
        public int ArmorCapPerSlot = 9;
        public bool Locked;
        public bool IsPreview;

        public void FromLoadout(PlayerLoadout loadout)
        {
            Clear();
            IsPreview = false;
            if (loadout == null) return;

            for (int i = 0; i < Weapons.Length; i++) Weapons[i] = loadout.GetWeapon(i);
            for (int i = 0; i < loadout.ArmorSlotCount; i++)
            {
                ArmorSlots.Add(loadout.ArmorSlots[i]);
                Armor.Add(loadout.GetArmor(i));
            }

            var inv = loadout.Inventory;
            if (inv != null)
            {
                CarriedWeapons.AddRange(inv.Weapons);
                CarriedArmor.AddRange(inv.Armor);
                WeaponCapPerSlot = inv.CarriedWeaponCapPerSlot;
                ArmorCapPerSlot = inv.CarriedArmorCapPerSlot;
            }
            Locked = loadout.IsLocked;
        }

        // Rolls fresh instances; the screen keeps the result so a preview doesn't reroll on every rebuild.
        public void FromStarting(StartingLoadoutSO start, IList<ArmorSlotSO> slots)
        {
            Clear();
            IsPreview = true;
            if (slots != null) foreach (var s in slots) if (s != null) { ArmorSlots.Add(s); Armor.Add(null); }
            if (start == null) return;

            var mode = start.rollMode;
            if (start.equipped != null)
                for (int i = 0; i < start.equipped.Length && i < Weapons.Length; i++)
                    if (start.equipped[i] != null) Weapons[i] = new WeaponInstance(start.equipped[i], mode);

            foreach (var w in start.carried) if (w != null) CarriedWeapons.Add(new WeaponInstance(w, mode));

            foreach (var a in start.equippedArmor)
            {
                if (a == null) continue;
                int idx = ArmorSlots.IndexOf(a.slot);
                if (idx >= 0 && Armor[idx] == null) Armor[idx] = new ArmorInstance(a, mode);
                else CarriedArmor.Add(new ArmorInstance(a, mode));
            }
            foreach (var a in start.carriedArmor) if (a != null) CarriedArmor.Add(new ArmorInstance(a, mode));
        }

        public void Clear()
        {
            for (int i = 0; i < Weapons.Length; i++) Weapons[i] = null;
            ArmorSlots.Clear();
            Armor.Clear();
            CarriedWeapons.Clear();
            CarriedArmor.Clear();
            Locked = false;
        }

        public IEnumerable<ArmorInstance> EquippedArmor()
        {
            foreach (var a in Armor) if (a != null) yield return a;
        }

        public object Equipped(SlotRef slot)
        {
            if (slot.Kind == SlotKind.Weapon)
                return slot.Index >= 0 && slot.Index < Weapons.Length ? Weapons[slot.Index] : null;
            return slot.Index >= 0 && slot.Index < Armor.Count ? Armor[slot.Index] : null;
        }

        // Carried items that can go in this slot; for weapons, the slot's own inventory slot first.
        public void CarriedFor(SlotRef slot, List<object> into)
        {
            into.Clear();
            if (slot.Kind == SlotKind.Weapon)
            {
                foreach (var w in CarriedWeapons)
                    if (Allowed(w, slot.Index) && w.Definition.InventorySlot == slot.Index) into.Add(w);
                foreach (var w in CarriedWeapons)
                    if (Allowed(w, slot.Index) && w.Definition.InventorySlot != slot.Index) into.Add(w);
                return;
            }

            var armorSlot = slot.Index >= 0 && slot.Index < ArmorSlots.Count ? ArmorSlots[slot.Index] : null;
            foreach (var a in CarriedArmor)
                if (a.Slot == armorSlot) into.Add(a);
        }

        // How many carried items count against this slot's cap.
        public int CarriedCount(SlotRef slot)
        {
            int n = 0;
            if (slot.Kind == SlotKind.Weapon)
            {
                foreach (var w in CarriedWeapons)
                    if (w.Definition != null && w.Definition.InventorySlot == slot.Index) n++;
                return n;
            }

            var armorSlot = slot.Index >= 0 && slot.Index < ArmorSlots.Count ? ArmorSlots[slot.Index] : null;
            foreach (var a in CarriedArmor) if (a.Slot == armorSlot) n++;
            return n;
        }

        public int Cap(SlotRef slot) => slot.Kind == SlotKind.Weapon ? WeaponCapPerSlot : ArmorCapPerSlot;

        private static bool Allowed(WeaponInstance w, int slot)
            => w != null && w.Definition != null && LoadoutSlots.Allows(w.Definition.AllowedSlots, slot);
    }
}
