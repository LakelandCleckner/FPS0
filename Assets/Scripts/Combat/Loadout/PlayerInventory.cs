using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Weapons;

namespace Combat.Loadout
{
    // What the player carries but has not equipped, plus shared ammo pools. Data only.
    public class PlayerInventory : MonoBehaviour
    {
        [Tooltip("Weapons carried outside the loadout. Equipped weapons don't count.")]
        [Min(0)]
        [SerializeField] private int carriedWeaponCap = 9;

        [Tooltip("Armour pieces carried per armour slot. Equipped armour doesn't count.")]
        [Min(0)]
        [SerializeField] private int carriedArmorCapPerSlot = 9;

        private readonly List<WeaponInstance> weapons = new List<WeaponInstance>();
        private readonly List<ArmorInstance> armor = new List<ArmorInstance>();
        private readonly Dictionary<AmmoPoolSO, int> pools = new Dictionary<AmmoPoolSO, int>();

        public event Action Changed;
        public event Action<AmmoPoolSO> PoolChanged;

        public IReadOnlyList<WeaponInstance> Weapons => weapons;
        public int CarriedWeaponCap => carriedWeaponCap;
        public bool HasWeaponSpace => weapons.Count < carriedWeaponCap;

        public IReadOnlyList<ArmorInstance> Armor => armor;
        public int CarriedArmorCapPerSlot => carriedArmorCapPerSlot;

        // ------------------------------------------------------------------ weapons

        public bool Contains(WeaponInstance weapon) => weapon != null && weapons.Contains(weapon);

        public bool TryAddWeapon(WeaponInstance weapon)
        {
            if (weapon == null || weapon.Definition == null) return false;
            if (weapons.Contains(weapon) || !HasWeaponSpace) return false;

            weapons.Add(weapon);
            Changed?.Invoke();
            return true;
        }

        public bool RemoveWeapon(WeaponInstance weapon)
        {
            if (weapon == null || !weapons.Remove(weapon)) return false;
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ armour

        public bool Contains(ArmorInstance piece) => piece != null && armor.Contains(piece);

        public int CountArmor(ArmorSlotSO slot)
        {
            int n = 0;
            for (int i = 0; i < armor.Count; i++)
                if (armor[i].Slot == slot) n++;
            return n;
        }

        public bool HasArmorSpace(ArmorSlotSO slot) => slot != null && CountArmor(slot) < carriedArmorCapPerSlot;

        public bool TryAddArmor(ArmorInstance piece)
        {
            if (piece == null || piece.Slot == null) return false;
            if (armor.Contains(piece) || !HasArmorSpace(piece.Slot)) return false;

            armor.Add(piece);
            Changed?.Invoke();
            return true;
        }

        public bool RemoveArmor(ArmorInstance piece)
        {
            if (piece == null || !armor.Remove(piece)) return false;
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ ammo pools

        public int GetAmmo(AmmoPoolSO pool)
            => pool != null && pools.TryGetValue(pool, out var amount) ? amount : 0;

        public void SetAmmo(AmmoPoolSO pool, int amount)
        {
            if (pool == null) return;

            amount = Mathf.Max(0, amount);
            if (pool.capacity > 0) amount = Mathf.Min(amount, pool.capacity);

            if (pools.TryGetValue(pool, out var current) && current == amount) return;

            pools[pool] = amount;
            PoolChanged?.Invoke(pool);
        }

        public void AddAmmo(AmmoPoolSO pool, int amount) => SetAmmo(pool, GetAmmo(pool) + amount);
    }
}
