using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Events;
using Combat.Sources;
using Combat.Stats;

namespace Combat.Weapons
{
    // Owns the player's weapons and the swap between them.
    //
    // STOWED WEAPONS STAY ALIVE. Not SetActive(false) — WeaponEventBridge subscribes
    // in OnEnable and unsubscribes in OnDisable, so a deactivated weapon goes deaf and
    // the Always perk scope (doc 07 §9) stops working, which is exactly what a
    // Bait-and-Switch-style perk needs. A stowed weapon also has to keep running its
    // own Update so an Auto-Loading-Holster-style perk can reload it.
    //
    // So "stowed" means: model hidden, input gated (WeaponFireController.IsActive),
    // component alive and subscribed.
    //
    // The loadout is also the authoritative origin for Equip and Stow (doc 07 §2),
    // and the natural registry for anything player-side that modifies weapons —
    // armour granting handling or magazine size registers onto each weapon's
    // container from here, keyed by the armour piece as modifier Owner.
    public class WeaponLoadout : MonoBehaviour
    {
        [Serializable]
        public class Slot
        {
            public WeaponFireController controller;

            [Tooltip("Optional. Where to look for renderers to hide while stowed. " +
                     "Leave empty to use the controller's own object and its children, " +
                     "which is right when the scripts and the mesh live together.")]
            public Transform modelRoot;

            // Captured once at startup: the renderers that were visible then. Toggled
            // by enabled rather than by deactivating a GameObject, because the scripts
            // and the mesh are typically on the SAME object — SetActive(false) there
            // would unsubscribe WeaponEventBridge and StatusReceiver in OnDisable and
            // the stowed weapon would go deaf.
            //
            // Only renderers that were enabled at capture are managed, so a part
            // deliberately hidden by the artist stays hidden on equip.
            [NonSerialized] public Renderer[] renderers;

            // Disabled while stowed so an invisible weapon isn't evaluating clips —
            // and so it can't come back up frozen mid-reload. The COMPONENT is
            // disabled, never the GameObject, for the same subscription reason.
            [NonSerialized] public Animator animator;
        }

        [Header("Weapons")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        [Header("Stats")]
        [SerializeField] private LoadoutStatKeys statKeys;

        [Tooltip("Used when a weapon has no equip_time stat wired.")]
        [SerializeField] private float fallbackEquipTime = 0.5f;
        [Tooltip("Used when a weapon has no stow_time stat wired.")]
        [SerializeField] private float fallbackStowTime = 0.4f;

        [Header("Input")]
        [SerializeField] private Key swapKey = Key.Q;
        [Tooltip("Digit keys select a slot directly. 1 selects slot 0, and so on.")]
        [SerializeField] private bool enableDigitSelect = true;

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private enum SwapState { Ready, Stowing, Equipping }

        private SwapState state = SwapState.Ready;
        private int activeIndex;
        private int previousIndex;
        private int pendingIndex = -1;

        private float timer;
        private float duration;

        private WeaponEventBus bus;

        // Presentation hook. Fired when a weapon becomes the held one — on startup and
        // at the end of every equip. Direct C# event rather than the bus for the same
        // reason WeaponAnimator uses one: local, immediate, allocation-free, and a HUD
        // re-target isn't the owner-scoped view perks need.
        public event Action<WeaponFireController> OnWeaponEquipped;

        public int ActiveIndex => activeIndex;
        public bool IsSwapping => state != SwapState.Ready;

        /// The weapon currently held. Null only if the loadout is empty.
        public WeaponFireController Active =>
            IsValid(activeIndex) ? slots[activeIndex].controller : null;

        private void Awake()
        {
            bus = WeaponEventBus.FindFor(this);
        }

        private void Start()
        {
            for (int i = 0; i < slots.Count; i++)
                CacheRenderers(i);

            for (int i = 0; i < slots.Count; i++)
            {
                if (!IsValid(i))
                    Debug.LogError($"[Loadout] Slot {i} has no WeaponFireController assigned.");
                else if (debugLog)
                    Debug.Log($"[Loadout] Slot {i}: {slots[i].controller.name} " +
                              $"equip={EquipTime(i):F2}s stow={StowTime(i):F2}s " +
                              $"renderers={(slots[i].renderers != null ? slots[i].renderers.Length : 0)}");
            }

            if (slots.Count < 2)
                Debug.LogWarning("[Loadout] Fewer than 2 slots — swapping is disabled.");

            // Everything starts stowed except slot 0, which starts ready. Doing this
            // in Start (not Awake) lets each weapon's own Awake/Start run first, so
            // its container exists before we resolve times off it.
            for (int i = 0; i < slots.Count; i++)
                ApplyHeldState(i, held: i == activeIndex, ready: i == activeIndex);

            if (Active != null)
            {
                PublishFor(activeIndex, WeaponEventType.Equip);
                OnWeaponEquipped?.Invoke(Active);
            }
        }

        private void Update()
        {
            ReadInput();
            TickSwap();
        }

        // ------------------------------------------------------------------ input

        private void ReadInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[swapKey].wasPressedThisFrame)
                RequestSwap(SwapTarget());

            if (!enableDigitSelect) return;

            // digit1..digit4 -> slots 0..3
            if (kb.digit1Key.wasPressedThisFrame) RequestSwap(0);
            else if (kb.digit2Key.wasPressedThisFrame) RequestSwap(1);
            else if (kb.digit3Key.wasPressedThisFrame) RequestSwap(2);
            else if (kb.digit4Key.wasPressedThisFrame) RequestSwap(3);
        }

        // ------------------------------------------------------------------ swap

        // What the swap key means: "the weapon I was last holding". At startup there
        // isn't one — previousIndex and activeIndex both start at 0 — so fall back to
        // the next valid slot in order. Without this the first press asks to swap to
        // the weapon already in hand and is silently ignored.
        private int SwapTarget()
        {
            if (previousIndex != activeIndex && IsValid(previousIndex))
                return previousIndex;

            for (int i = 1; i <= slots.Count; i++)
            {
                int idx = (activeIndex + i) % slots.Count;
                if (IsValid(idx)) return idx;
            }
            return activeIndex;
        }

        public void RequestSwap(int targetIndex)
        {
            if (debugLog)
                Debug.Log($"[Loadout] RequestSwap target={targetIndex} active={activeIndex} " +
                          $"prev={previousIndex} state={state} slots={slots.Count}");

            if (!IsValid(targetIndex))
            {
                if (debugLog) Debug.Log($"[Loadout]   rejected: slot {targetIndex} invalid");
                return;
            }
            if (slots.Count < 2)
            {
                if (debugLog) Debug.Log("[Loadout]   rejected: need 2+ slots");
                return;
            }

            switch (state)
            {
                case SwapState.Ready:
                    if (targetIndex == activeIndex)
                    {
                        if (debugLog) Debug.Log("[Loadout]   rejected: already holding it");
                        return;
                    }
                    BeginStow(targetIndex);
                    break;

                case SwapState.Stowing:
                    // Asking for the weapon we're already putting away = the classic
                    // double-tap cancel. Reverse: bring it back up, carrying progress,
                    // so a quick cancel is genuinely quick instead of paying a full
                    // equip. Nothing ever changed hands, so no events fire.
                    if (targetIndex == activeIndex)
                    {
                        float stowProgress = Progress();
                        state = SwapState.Equipping;
                        duration = EquipTime(activeIndex);
                        timer = (1f - stowProgress) * duration;
                        pendingIndex = -1;
                    }
                    else
                    {
                        // Redirect mid-stow: same animation, different destination.
                        pendingIndex = targetIndex;
                    }
                    break;

                case SwapState.Equipping:
                    // Already raising something. Put it back down and swap again,
                    // carrying progress the same way.
                    if (targetIndex == activeIndex) return;
                    {
                        float equipProgress = Progress();
                        BeginStow(targetIndex);
                        timer = (1f - equipProgress) * duration;
                    }
                    break;
            }
        }

        private void BeginStow(int targetIndex)
        {
            pendingIndex = targetIndex;
            state = SwapState.Stowing;
            duration = StowTime(activeIndex);
            timer = 0f;

            if (debugLog)
                Debug.Log($"[Loadout] STOW {activeIndex} -> {targetIndex} over {duration:F2}s");

            // Gate input the instant the swap starts — the weapon is on its way down.
            SetReady(activeIndex, false);

            // Stowing cancels an in-progress reload. This is a ONE-OFF on the
            // transition, not a standing rule: a stowed weapon must still be able to
            // reload, or Auto-Loading Holster could never work.
            AmmoOf(activeIndex)?.CancelReload();
        }

        private void TickSwap()
        {
            if (state == SwapState.Ready) return;

            timer += Time.deltaTime;
            if (timer < duration) return;

            if (state == SwapState.Stowing)
            {
                int outgoing = activeIndex;
                int incoming = IsValid(pendingIndex) ? pendingIndex : activeIndex;

                ApplyHeldState(outgoing, held: false, ready: false);
                PublishFor(outgoing, WeaponEventType.Stow);

                previousIndex = outgoing;
                activeIndex = incoming;
                pendingIndex = -1;

                // Show the incoming model immediately; it's coming up now.
                ApplyHeldState(activeIndex, held: true, ready: false);

                state = SwapState.Equipping;
                duration = EquipTime(activeIndex);
                timer = 0f;
                return;
            }

            // Equipping finished
            state = SwapState.Ready;
            timer = 0f;
            SetReady(activeIndex, true);
            PublishFor(activeIndex, WeaponEventType.Equip);
            OnWeaponEquipped?.Invoke(Active);

            if (debugLog) Debug.Log($"[Loadout] READY {activeIndex}");
        }

        private float Progress() => duration > 0f ? Mathf.Clamp01(timer / duration) : 1f;

        // ------------------------------------------------------------------ state

        // held  -> model visible and this is the weapon in hand
        // ready -> may accept fire/reload input (false during a swap animation)
        private void ApplyHeldState(int index, bool held, bool ready)
        {
            if (!IsValid(index)) return;
            var slot = slots[index];

            SetVisible(slot, held);

            SetReady(index, ready);

            var ammo = AmmoOf(index);
            if (ammo != null) ammo.IsHeld = held;
        }

        private void CacheRenderers(int index)
        {
            if (!IsValid(index)) return;
            var slot = slots[index];

            var root = slot.modelRoot != null ? slot.modelRoot : slot.controller.transform;

            var all = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            var visible = new List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) visible.Add(all[i]);

            slot.renderers = visible.ToArray();
            slot.animator = root.GetComponentInChildren<Animator>(includeInactive: true);
        }

        private static void SetVisible(Slot slot, bool visible)
        {
            if (slot.renderers != null)
            {
                for (int i = 0; i < slot.renderers.Length; i++)
                {
                    var r = slot.renderers[i];
                    if (r != null) r.enabled = visible;
                }
            }

            if (slot.animator == null) return;

            if (!visible)
            {
                slot.animator.enabled = false;
                return;
            }

            // Coming back up: enable, then Rebind to snap the rig to its default
            // state. Without this the animator resumes exactly where it froze, so a
            // weapon stowed mid-reload would reappear holding that pose. Update(0)
            // applies the reset on this frame rather than the next, avoiding one
            // frame of stale pose as the weapon becomes visible.
            slot.animator.enabled = true;
            slot.animator.Rebind();
            slot.animator.Update(0f);
        }

        private void SetReady(int index, bool ready)
        {
            if (!IsValid(index)) return;
            var c = slots[index].controller;
            if (c != null) c.IsActive = ready;
        }

        // ------------------------------------------------------------------ stats

        private float EquipTime(int index) =>
            ResolveTime(index, statKeys != null ? statKeys.equipTime : null, fallbackEquipTime);

        private float StowTime(int index) =>
            ResolveTime(index, statKeys != null ? statKeys.stowTime : null, fallbackStowTime);

        // Resolved off the WEAPON's container, so a per-weapon handling roll, a
        // Quick-Access-Sling-style weapon perk and an armour modifier all compose
        // through the same pipeline. Cached read; falls back if unwired so a missing
        // stat can never produce a zero-length or infinite swap.
        private float ResolveTime(int index, StatDefinitionSO def, float fallback)
        {
            if (!IsValid(index) || def == null) return fallback;

            var source = slots[index].controller != null
                ? slots[index].controller.DamageSource : null;
            var container = source != null ? source.SourceStats : null;
            if (container == null) return fallback;

            float v = container.Resolve(def);
            return v > 0f ? v : fallback;
        }

        // ------------------------------------------------------------------ misc

        private bool IsValid(int i) => i >= 0 && i < slots.Count && slots[i] != null
                                       && slots[i].controller != null;

        private WeaponAmmo AmmoOf(int index)
        {
            if (!IsValid(index)) return null;
            return slots[index].controller.GetComponent<WeaponAmmo>();
        }

        private void PublishFor(int index, WeaponEventType type)
        {
            if (bus == null || !IsValid(index)) return;
            var source = slots[index].controller.DamageSource;
            if (source == null) return;
            bus.Publish(WeaponEvent.ForWeapon(type, source));
        }
    }
}