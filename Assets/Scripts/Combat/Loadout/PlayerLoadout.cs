using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;
using Combat.Weapons;

namespace Combat.Loadout
{
    // What the player has equipped. The model only; WeaponLoadout and LoadoutModifierApplier act on it.
    public class PlayerLoadout : MonoBehaviour
    {
        [Tooltip("Found on this object if empty.")]
        [SerializeField] private PlayerInventory inventory;

        [SerializeField] private StartingLoadoutSO startingLoadout;

        [Header("Armor")]
        [Tooltip("Armor slots in display order. Add a slot asset here to add a slot.")]
        [SerializeField] private List<ArmorSlotSO> armorSlots = new List<ArmorSlotSO>();

        private readonly WeaponInstance[] weapons = new WeaponInstance[LoadoutSlots.Count];
        private ArmorInstance[] armor = new ArmorInstance[0];
        private readonly HashSet<object> locks = new HashSet<object>();
        private bool initialized;

        // slot, old, new
        public event Action<int, WeaponInstance, WeaponInstance> WeaponSlotChanged;
        public event Action<ArmorSlotSO, ArmorInstance, ArmorInstance> ArmorSlotChanged;

        // True when the first lock is taken, false when the last is released.
        public event Action<bool> LockChanged;

        public PlayerInventory Inventory => inventory;
        public bool IsLocked => locks.Count > 0;

        public int WeaponSlotCount => weapons.Length;
        public int ArmorSlotCount => armor.Length;
        public IReadOnlyList<ArmorSlotSO> ArmorSlots => armorSlots;

        public WeaponInstance GetWeapon(int slot)
            => slot >= 0 && slot < weapons.Length ? weapons[slot] : null;

        public int SlotOf(WeaponInstance weapon)
        {
            if (weapon == null) return -1;
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] == weapon) return i;
            return -1;
        }

        public int ArmorSlotIndex(ArmorSlotSO slot)
        {
            int i = slot != null ? armorSlots.IndexOf(slot) : -1;
            return i < armor.Length ? i : -1;
        }

        public ArmorInstance GetArmor(int index)
            => index >= 0 && index < armor.Length ? armor[index] : null;

        public ArmorInstance GetArmor(ArmorSlotSO slot) => GetArmor(ArmorSlotIndex(slot));

        private void Awake() => EnsureInitialized();

        // Idempotent; WeaponLoadout and the modifier applier call it too so script order doesn't matter.
        public void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (inventory == null) Debug.LogError("[PlayerLoadout] No PlayerInventory found.");

            armor = new ArmorInstance[armorSlots.Count];

            if (startingLoadout == null) return;

            if (inventory != null)
                foreach (var p in startingLoadout.ammoPools)
                    inventory.SetAmmo(p.pool, p.amount);

            if (startingLoadout.randomize) { RandomFill(); return; }

            var equipped = startingLoadout.equipped;
            if (equipped != null)
            {
                for (int i = 0; i < equipped.Length && i < weapons.Length; i++)
                {
                    var def = equipped[i];
                    if (def == null) continue;

                    if (!LoadoutSlots.Allows(def.AllowedSlots, i))
                    {
                        Debug.LogWarning($"[PlayerLoadout] '{def.name}' isn't allowed in slot {i + 1}; carrying it instead.");
                        AddCarried(def);
                        continue;
                    }

                    weapons[i] = new WeaponInstance(def, StartRoll());
                }
            }

            foreach (var def in startingLoadout.carried)
                if (def != null) AddCarried(def);

            foreach (var def in startingLoadout.equippedArmor)
            {
                if (def == null) continue;

                int idx = ArmorSlotIndex(def.slot);
                if (idx < 0)
                {
                    Debug.LogWarning($"[PlayerLoadout] '{def.name}' has no slot on this loadout; carrying it instead.");
                    AddCarriedArmor(def);
                }
                else if (armor[idx] != null) AddCarriedArmor(def);
                else armor[idx] = new ArmorInstance(def, StartRoll());
            }

            foreach (var def in startingLoadout.carriedArmor)
                if (def != null) AddCarriedArmor(def);
        }

        // Testing: a random allowed weapon per slot (no repeats while possible), a random piece per armor slot, random carried extras.
        private void RandomFill()
        {
            var weaponPool = startingLoadout.WeaponPool();
            var armorPool = startingLoadout.ArmorPool();
            weaponPool.RemoveAll(w => w.archetype == null);
            var used = new List<WeaponSO>();

            for (int i = 0; i < weapons.Length; i++)
            {
                int slot = i;
                var def = Pick(weaponPool, w => LoadoutSlots.Allows(w.AllowedSlots, slot) && !used.Contains(w))
                       ?? Pick(weaponPool, w => LoadoutSlots.Allows(w.AllowedSlots, slot));
                if (def == null) continue;
                used.Add(def);
                weapons[i] = new WeaponInstance(def, StartRoll());
            }

            for (int i = 0; i < armor.Length; i++)
            {
                var slot = armorSlots[i];
                var def = Pick(armorPool, a => a.slot == slot);
                if (def != null) armor[i] = new ArmorInstance(def, StartRoll());
            }

            if (inventory == null) return;
            for (int n = 0; n < startingLoadout.randomCarriedWeapons; n++)
            {
                var def = Pick(weaponPool, null);
                if (def != null) inventory.TryAddWeapon(new WeaponInstance(def, StartRoll()));
            }
            for (int n = 0; n < startingLoadout.randomCarriedArmor; n++)
            {
                var def = Pick(armorPool, a => ArmorSlotIndex(a.slot) >= 0);
                if (def != null) inventory.TryAddArmor(new ArmorInstance(def, StartRoll()));
            }
        }

        private static T Pick<T>(List<T> pool, Predicate<T> ok) where T : class
        {
            int count = 0;
            T chosen = null;
            foreach (var x in pool)
            {
                if (ok != null && !ok(x)) continue;
                if (UnityEngine.Random.Range(0, ++count) == 0) chosen = x;   // reservoir pick, uniform
            }
            return chosen;
        }

        private RollMode StartRoll() => startingLoadout != null ? startingLoadout.rollMode : RollMode.Random;

        private void AddCarried(WeaponSO def)
        {
            if (inventory == null || !inventory.TryAddWeapon(new WeaponInstance(def, StartRoll())))
                Debug.LogWarning($"[PlayerLoadout] No carried space for starting weapon '{def.name}'.");
        }

        private void AddCarriedArmor(ArmorSO def)
        {
            if (inventory == null || !inventory.TryAddArmor(new ArmorInstance(def, StartRoll())))
                Debug.LogWarning($"[PlayerLoadout] No carried space for starting armor '{def.name}'.");
        }

        // ------------------------------------------------------------------ weapons

        // Puts a weapon in a slot; the previous occupant goes to carried inventory.
        public bool TryEquip(int slot, WeaponInstance weapon, out string reason)
        {
            reason = null;

            if (IsLocked) { reason = "Loadout is locked."; return false; }
            if (slot < 0 || slot >= weapons.Length) { reason = "Invalid slot."; return false; }
            if (weapon == null || weapon.Definition == null) { reason = "No weapon."; return false; }
            if (weapons[slot] == weapon) return true;
            if (SlotOf(weapon) >= 0) { reason = "Already equipped in another slot."; return false; }

            if (!LoadoutSlots.Allows(weapon.Definition.AllowedSlots, slot))
            {
                reason = "Not allowed in this slot.";
                return false;
            }

            var old = weapons[slot];
            bool fromInventory = inventory != null && inventory.Contains(weapon);

            // Taking one out of carried frees space only in its own inventory slot.
            if (old != null && !HasRoomForOutgoing(old, fromInventory ? weapon : null))
            {
                reason = "Inventory is full.";
                return false;
            }

            if (fromInventory) inventory.RemoveWeapon(weapon);
            if (old != null) inventory.TryAddWeapon(old);

            weapons[slot] = weapon;
            WeaponSlotChanged?.Invoke(slot, old, weapon);
            return true;
        }

        private bool HasRoomForOutgoing(WeaponInstance outgoing, WeaponInstance leavingInventory)
        {
            if (inventory == null || outgoing.Definition == null) return false;

            int bucket = outgoing.Definition.InventorySlot;
            int count = inventory.CountWeapons(bucket);
            if (leavingInventory != null && leavingInventory.Definition != null
                && leavingInventory.Definition.InventorySlot == bucket) count--;
            return count < inventory.CarriedWeaponCapPerSlot;
        }

        // Empties a slot; the weapon goes to carried inventory.
        public bool TryClear(int slot, out string reason)
        {
            reason = null;

            if (IsLocked) { reason = "Loadout is locked."; return false; }
            if (slot < 0 || slot >= weapons.Length) { reason = "Invalid slot."; return false; }

            var old = weapons[slot];
            if (old == null) return true;

            if (inventory == null || !inventory.TryAddWeapon(old))
            {
                reason = "Inventory is full.";
                return false;
            }

            weapons[slot] = null;
            WeaponSlotChanged?.Invoke(slot, old, null);
            return true;
        }

        // ------------------------------------------------------------------ armor

        // Puts armor in its own slot; the previous piece goes to carried inventory.
        public bool TryEquipArmor(ArmorInstance piece, out string reason)
        {
            reason = null;

            if (IsLocked) { reason = "Loadout is locked."; return false; }
            if (piece == null || piece.Definition == null) { reason = "No armor."; return false; }

            int idx = ArmorSlotIndex(piece.Slot);
            if (idx < 0) { reason = "This armor's slot isn't on the loadout."; return false; }
            if (armor[idx] == piece) return true;

            var old = armor[idx];
            bool fromInventory = inventory != null && inventory.Contains(piece);

            // Same slot, so taking one out of carried frees the space the old one needs.
            if (old != null && !fromInventory && (inventory == null || !inventory.HasArmorSpace(piece.Slot)))
            {
                reason = "Inventory is full.";
                return false;
            }

            if (fromInventory) inventory.RemoveArmor(piece);
            if (old != null) inventory.TryAddArmor(old);

            armor[idx] = piece;
            ArmorSlotChanged?.Invoke(armorSlots[idx], old, piece);
            return true;
        }

        // Empties an armor slot; the piece goes to carried inventory.
        public bool TryClearArmor(int index, out string reason)
        {
            reason = null;

            if (IsLocked) { reason = "Loadout is locked."; return false; }
            if (index < 0 || index >= armor.Length) { reason = "Invalid armor slot."; return false; }

            var old = armor[index];
            if (old == null) return true;

            if (inventory == null || !inventory.TryAddArmor(old))
            {
                reason = "Inventory is full.";
                return false;
            }

            armor[index] = null;
            ArmorSlotChanged?.Invoke(armorSlots[index], old, null);
            return true;
        }

        public bool TryClearArmor(ArmorSlotSO slot, out string reason)
            => TryClearArmor(ArmorSlotIndex(slot), out reason);

        // ------------------------------------------------------------------ locks

        // Any object can hold a lock (an activity, a cutscene); edits fail while any is held.
        public void AddLock(object reason)
        {
            if (reason == null) return;
            bool was = IsLocked;
            if (locks.Add(reason) && !was) LockChanged?.Invoke(true);
        }

        public void RemoveLock(object reason)
        {
            if (reason == null) return;
            if (locks.Remove(reason) && !IsLocked) LockChanged?.Invoke(false);
        }
    }
}
