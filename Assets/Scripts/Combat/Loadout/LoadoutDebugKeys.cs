using UnityEngine;
using System.Collections.Generic;
using System.Text;
using Combat.Armor;
using Combat.Loot;
using Combat.Perks;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Loadout
{
    // Loadout debug commands. Public so a debug panel can call them; keys come from the Debug input map.
    public class LoadoutDebugKeys : MonoBehaviour
    {
        [Tooltip("Found in parents if empty.")]
        [SerializeField] private PlayerLoadout loadout;

        [Tooltip("For logging armor stat results. Found in parents if empty.")]
        [SerializeField] private CombatantStats player;
        [Tooltip("For logging armor stat results on the held weapon. Found in parents/children if empty.")]
        [SerializeField] private WeaponLoadout weapons;

        [Tooltip("Rolled by the spawn-drop key.")]
        [SerializeField] private LootTableSO debugLootTable;
        [SerializeField] private float debugDropDistance = 3f;

        private readonly object debugLock = new object();
        private PlayerInputRouter input;

        private void Awake()
        {
            if (loadout == null) loadout = GetComponentInParent<PlayerLoadout>();
            if (player == null) player = GetComponentInParent<CombatantStats>();
            if (weapons == null) weapons = GetComponentInParent<WeaponLoadout>();
            if (weapons == null && loadout != null) weapons = loadout.GetComponentInChildren<WeaponLoadout>();
            input = PlayerInputRouter.For(this);
        }

        private void Update()
        {
            if (input == null) input = PlayerInputRouter.For(this);
            if (input == null || loadout == null) return;

            var d = input.Debug;
            if (d.CycleSlot1.WasPressedThisFrame()) CycleWeapon(0);
            if (d.CycleSlot2.WasPressedThisFrame()) CycleWeapon(1);
            if (d.CycleSlot3.WasPressedThisFrame()) CycleWeapon(2);
            if (d.ToggleLock.WasPressedThisFrame()) ToggleLock();
            if (d.ClearSlot1.WasPressedThisFrame()) ClearWeapon(0);
            if (d.ClearSlot2.WasPressedThisFrame()) ClearWeapon(1);
            if (d.ClearSlot3.WasPressedThisFrame()) ClearWeapon(2);

            if (d.CycleArmor1.WasPressedThisFrame()) CycleArmor(0);
            if (d.CycleArmor2.WasPressedThisFrame()) CycleArmor(1);
            if (d.CycleArmor3.WasPressedThisFrame()) CycleArmor(2);
            if (d.CycleArmor4.WasPressedThisFrame()) CycleArmor(3);
            if (d.CycleArmor5.WasPressedThisFrame()) CycleArmor(4);
            if (d.ClearArmor1.WasPressedThisFrame()) ClearArmor(0);
            if (d.ClearArmor2.WasPressedThisFrame()) ClearArmor(1);
            if (d.ClearArmor3.WasPressedThisFrame()) ClearArmor(2);
            if (d.ClearArmor4.WasPressedThisFrame()) ClearArmor(3);
            if (d.ClearArmor5.WasPressedThisFrame()) ClearArmor(4);

            if (d.RerollHeld.WasPressedThisFrame()) RerollHeld();
            if (d.RerollArmor.WasPressedThisFrame()) RerollArmor();
            if (d.DamageSelf.WasPressedThisFrame()) DamageSelf();
            if (d.HealSelf.WasPressedThisFrame()) HealSelf();
            if (d.DumpWeaponStats.WasPressedThisFrame()) DumpWeaponStats();
            if (d.DumpPlayerStats.WasPressedThisFrame()) DumpPlayerStats();
            if (d.SpawnDrop.WasPressedThisFrame()) SpawnDrop();
        }

        // ------------------------------------------------------------------ weapons

        // Equips the first carried weapon allowed in the slot; the old one goes to the back.
        public void CycleWeapon(int slot)
        {
            var carried = loadout.Inventory != null ? loadout.Inventory.Weapons : null;
            if (carried == null || carried.Count == 0)
            {
                Debug.Log("[LoadoutDebug] Nothing carried.");
                return;
            }

            for (int i = 0; i < carried.Count; i++)
            {
                var w = carried[i];
                if (w == null || w.Definition == null) continue;
                if (!LoadoutSlots.Allows(w.Definition.AllowedSlots, slot)) continue;

                if (loadout.TryEquip(slot, w, out var reason))
                    Debug.Log($"[LoadoutDebug] Slot {slot + 1} <- {w.Definition.name}{PerkList(w.AllPerks())}");
                else
                    Debug.Log($"[LoadoutDebug] Slot {slot + 1}: {reason}");
                return;
            }

            Debug.Log($"[LoadoutDebug] No carried weapon is allowed in slot {slot + 1}.");
        }

        public void ClearWeapon(int slot)
        {
            if (loadout.TryClear(slot, out var reason))
                Debug.Log($"[LoadoutDebug] Slot {slot + 1} cleared.");
            else
                Debug.Log($"[LoadoutDebug] Slot {slot + 1}: {reason}");
        }

        // ------------------------------------------------------------------ armor

        // Equips the first carried piece for this armor slot; the old one goes to the back.
        public void CycleArmor(int index)
        {
            if (index >= loadout.ArmorSlotCount)
            {
                Debug.Log($"[LoadoutDebug] No armor slot {index + 1}.");
                return;
            }

            var slot = loadout.ArmorSlots[index];
            var carried = loadout.Inventory != null ? loadout.Inventory.Armor : null;

            if (carried != null)
            {
                for (int i = 0; i < carried.Count; i++)
                {
                    var a = carried[i];
                    if (a == null || a.Slot != slot) continue;

                    if (loadout.TryEquipArmor(a, out var reason))
                    {
                        Debug.Log($"[LoadoutDebug] {slot.name} <- {a.Definition.name}{PerkList(a.AllPerks())}");
                    }
                    else Debug.Log($"[LoadoutDebug] {slot.name}: {reason}");
                    return;
                }
            }

            Debug.Log($"[LoadoutDebug] No carried armor for {slot.name}.");
        }

        public void ClearArmor(int index)
        {
            var name = index < loadout.ArmorSlotCount ? loadout.ArmorSlots[index].name : $"armor slot {index + 1}";

            if (loadout.TryClearArmor(index, out var reason))
            {
                Debug.Log($"[LoadoutDebug] {name} cleared.");
            }
            else Debug.Log($"[LoadoutDebug] {name}: {reason}");
        }

        // ------------------------------------------------------------------ perks

        public void RerollHeld()
        {
            var held = weapons != null ? weapons.Active : null;
            var instance = held != null && held.DamageSource != null ? held.DamageSource.Instance : null;
            if (instance == null) { Debug.Log("[LoadoutDebug] Nothing held to reroll."); return; }

            instance.Reroll();
            var perks = held.GetComponent<WeaponPerks>();
            if (perks != null) perks.Reattach();
            else Debug.LogWarning("[LoadoutDebug] Held weapon has no WeaponPerks component.");

            Debug.Log($"[LoadoutDebug] Rerolled {instance.Definition.name}{PerkList(instance.AllPerks())}");
        }

        public void RerollArmor()
        {
            var host = loadout.GetComponent<ArmorPerks>();
            for (int i = 0; i < loadout.ArmorSlotCount; i++)
            {
                var a = loadout.GetArmor(i);
                if (a == null) continue;

                a.Reroll();
                if (host != null) host.Reattach(a);
                Debug.Log($"[LoadoutDebug] Rerolled {a.Definition.name}{PerkList(a.AllPerks())}");
            }
            if (host == null) Debug.LogWarning("[LoadoutDebug] No ArmorPerks on the player.");
        }

        private static string PerkList(IEnumerable<PerkSO> perks)
        {
            var sb = new StringBuilder();
            foreach (var p in perks)
                sb.Append(sb.Length == 0 ? " [" : ", ").Append(p.name);
            return sb.Length == 0 ? " [no perks]" : sb.Append(']').ToString();
        }

        // ------------------------------------------------------------------ stat dump

        public void DumpWeaponStats() => DumpStats(false);
        public void DumpPlayerStats() => DumpStats(true);

        private void DumpStats(bool playerStats)
        {
            StatContainer c;
            string who;

            if (playerStats)
            {
                c = player != null ? player.Container : null;
                who = "Player";
            }
            else
            {
                var held = weapons != null ? weapons.Active : null;
                c = held != null && held.DamageSource != null ? held.DamageSource.SourceStats : null;
                who = held != null ? held.name : "No weapon";
            }

            if (c == null) { Debug.Log($"[LoadoutDebug] {who}: no stat container."); return; }

            var sb = new StringBuilder();
            sb.Append("[LoadoutDebug] ").Append(who).Append(" stats\n");
            for (int i = 0; i < StatRegistry.Count; i++)
            {
                var stat = StatRegistry.GetByIndex(i);
                if (stat != null) c.DebugDescribe(stat, sb);
            }
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ health

        // 10% of max health, never lethal.
        public void DamageSelf() => ChangeHealth(-0.1f);
        public void HealSelf() => ChangeHealth(0.1f);

        private void ChangeHealth(float fraction)
        {
            var health = player != null ? player.GetComponent<CombatantHealth>() : null;
            if (health == null) { Debug.Log("[LoadoutDebug] No CombatantHealth on the player."); return; }

            float amount = health.MaxHealth * Mathf.Abs(fraction);
            if (fraction > 0f) health.Heal(amount);
            else health.TakeDamage(Mathf.Min(amount, health.CurrentHealth - 1f), BodyPart.Torso, null);

            Debug.Log($"[LoadoutDebug] Health {health.CurrentHealth:0}/{health.MaxHealth:0}");
        }

        // ------------------------------------------------------------------ loot

        // Rolls the debug loot table and drops it in front of the camera, ignoring drop chance.
        public void SpawnDrop()
        {
            if (debugLootTable == null) { Debug.Log("[LoadoutDebug] No debug loot table set."); return; }

            var cam = Camera.main;
            var from = cam != null ? cam.transform : loadout.transform;
            var forward = Vector3.ProjectOnPlane(from.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = loadout.transform.forward;

            var drop = debugLootTable.SpawnDrop(from.position + forward.normalized * debugDropDistance, true);
            if (drop != null) Debug.Log($"[LoadoutDebug] Dropped {drop.Label} ({drop.Rarity})");
            else Debug.Log("[LoadoutDebug] Loot table rolled nothing.");
        }

        // ------------------------------------------------------------------ lock

        public void ToggleLock()
        {
            if (loadout.IsLocked) loadout.RemoveLock(debugLock);
            else loadout.AddLock(debugLock);
            Debug.Log($"[LoadoutDebug] Locked: {loadout.IsLocked}");
        }
    }
}