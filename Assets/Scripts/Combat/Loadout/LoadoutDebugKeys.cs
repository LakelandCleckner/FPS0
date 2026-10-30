using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Armor;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Loadout
{
    // Test harness until there's UI.
    // F1-F3 cycle carried weapons into a slot, F5-F7 clear it, F4 toggles a lock.
    // Shift+F1-F5 cycle carried armour into an armour slot, Ctrl+F1-F5 clear it.
    public class LoadoutDebugKeys : MonoBehaviour
    {
        [Tooltip("Found in parents if empty.")]
        [SerializeField] private PlayerLoadout loadout;

        [Tooltip("For logging armour stat results. Found in parents if empty.")]
        [SerializeField] private CombatantStats player;
        [Tooltip("For logging armour stat results on the held weapon. Found in parents/children if empty.")]
        [SerializeField] private WeaponLoadout weapons;

        private readonly object debugLock = new object();

        private void Awake()
        {
            if (loadout == null) loadout = GetComponentInParent<PlayerLoadout>();
            if (player == null) player = GetComponentInParent<CombatantStats>();
            if (weapons == null) weapons = GetComponentInParent<WeaponLoadout>();
            if (weapons == null && loadout != null) weapons = loadout.GetComponentInChildren<WeaponLoadout>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || loadout == null) return;

            int f = PressedFKey(kb);
            if (f < 0) return;

            if (kb.shiftKey.isPressed) { if (f < 5) CycleArmor(f); return; }
            if (kb.ctrlKey.isPressed) { if (f < 5) ClearArmor(f); return; }

            if (f <= 2) Cycle(f);
            else if (f == 3) ToggleLock();
            else Clear(f - 4);
        }

        // F1..F7 -> 0..6, -1 if none.
        private static int PressedFKey(Keyboard kb)
        {
            if (kb.f1Key.wasPressedThisFrame) return 0;
            if (kb.f2Key.wasPressedThisFrame) return 1;
            if (kb.f3Key.wasPressedThisFrame) return 2;
            if (kb.f4Key.wasPressedThisFrame) return 3;
            if (kb.f5Key.wasPressedThisFrame) return 4;
            if (kb.f6Key.wasPressedThisFrame) return 5;
            if (kb.f7Key.wasPressedThisFrame) return 6;
            return -1;
        }

        // ------------------------------------------------------------------ weapons

        // Equips the first carried weapon allowed in the slot; the old one goes to the back.
        private void Cycle(int slot)
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
                    Debug.Log($"[LoadoutDebug] Slot {slot + 1} <- {w.Definition.name}");
                else
                    Debug.Log($"[LoadoutDebug] Slot {slot + 1}: {reason}");
                return;
            }

            Debug.Log($"[LoadoutDebug] No carried weapon is allowed in slot {slot + 1}.");
        }

        private void Clear(int slot)
        {
            if (loadout.TryClear(slot, out var reason))
                Debug.Log($"[LoadoutDebug] Slot {slot + 1} cleared.");
            else
                Debug.Log($"[LoadoutDebug] Slot {slot + 1}: {reason}");
        }

        // ------------------------------------------------------------------ armour

        // Equips the first carried piece for this armour slot; the old one goes to the back.
        private void CycleArmor(int index)
        {
            if (index >= loadout.ArmorSlotCount)
            {
                Debug.Log($"[LoadoutDebug] No armour slot {index + 1}.");
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
                        Debug.Log($"[LoadoutDebug] {slot.name} <- {a.Definition.name}");
                        LogStats(a.Definition);
                    }
                    else Debug.Log($"[LoadoutDebug] {slot.name}: {reason}");
                    return;
                }
            }

            Debug.Log($"[LoadoutDebug] No carried armour for {slot.name}.");
        }

        private void ClearArmor(int index)
        {
            var old = loadout.GetArmor(index);
            var name = index < loadout.ArmorSlotCount ? loadout.ArmorSlots[index].name : $"armour slot {index + 1}";

            if (loadout.TryClearArmor(index, out var reason))
            {
                Debug.Log($"[LoadoutDebug] {name} cleared.");
                if (old != null) LogStats(old.Definition);
            }
            else Debug.Log($"[LoadoutDebug] {name}: {reason}");
        }

        // Resolved values of every stat this armour touches, after the change.
        private void LogStats(ArmorSO def)
        {
            if (def == null) return;

            var held = weapons != null ? weapons.Active : null;
            var heldStats = held != null && held.DamageSource != null ? held.DamageSource.SourceStats : null;
            var playerStats = player != null ? player.Container : null;

            foreach (var e in def.modifiers)
            {
                if (e == null || e.stat == null) continue;

                bool onPlayer = e.target == ModifierTarget.Player;
                var c = onPlayer ? playerStats : heldStats;
                string where = onPlayer ? "player" : "held weapon";

                if (c == null) Debug.Log($"[LoadoutDebug]   {e.stat.name} ({where}): no container");
                else Debug.Log($"[LoadoutDebug]   {e.stat.name} ({where}) = {c.Resolve(e.stat):0.###}");
            }
        }

        // ------------------------------------------------------------------ lock

        private void ToggleLock()
        {
            if (loadout.IsLocked) loadout.RemoveLock(debugLock);
            else loadout.AddLock(debugLock);
            Debug.Log($"[LoadoutDebug] Locked: {loadout.IsLocked}");
        }
    }
}
