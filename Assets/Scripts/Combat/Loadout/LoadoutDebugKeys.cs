using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Weapons;

namespace Combat.Loadout
{
    // Test harness until there's UI. F1-F3 cycle carried weapons into a slot, F5-F7 clear it, F4 toggles a lock.
    public class LoadoutDebugKeys : MonoBehaviour
    {
        [Tooltip("Found in parents if empty.")]
        [SerializeField] private PlayerLoadout loadout;

        private readonly object debugLock = new object();

        private void Awake()
        {
            if (loadout == null) loadout = GetComponentInParent<PlayerLoadout>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || loadout == null) return;

            if (kb.f1Key.wasPressedThisFrame) Cycle(0);
            else if (kb.f2Key.wasPressedThisFrame) Cycle(1);
            else if (kb.f3Key.wasPressedThisFrame) Cycle(2);
            else if (kb.f5Key.wasPressedThisFrame) Clear(0);
            else if (kb.f6Key.wasPressedThisFrame) Clear(1);
            else if (kb.f7Key.wasPressedThisFrame) Clear(2);
            else if (kb.f4Key.wasPressedThisFrame) ToggleLock();
        }

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

        private void ToggleLock()
        {
            if (loadout.IsLocked) loadout.RemoveLock(debugLock);
            else loadout.AddLock(debugLock);
            Debug.Log($"[LoadoutDebug] Locked: {loadout.IsLocked}");
        }
    }
}
