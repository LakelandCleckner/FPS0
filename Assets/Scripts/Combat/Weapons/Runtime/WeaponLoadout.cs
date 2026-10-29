using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Events;
using Combat.Sources;
using Combat.Stats;

namespace Combat.Weapons
{
    // Owns the player's weapons and swapping between them; origin of Equip/Stow events.
    // Stowed weapons stay enabled (renderers/animator off) so perks and reloads keep running.
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

            // Renderers visible at startup; toggled via enabled so the weapon stays subscribed.
            [NonSerialized] public Renderer[] renderers;

            // Disabled while stowed so it can't resume mid-reload (component, not GameObject).
            [NonSerialized] public Animator animator;

            // Spawned gun model; set in Awake so WeaponAnimator can read it in Start.
            [NonSerialized] public GameObject gunInstance;
            [NonSerialized] public Animator gunAnimator;
            [NonSerialized] public RuntimeAnimatorController armsController;
            [NonSerialized] public Transform sight;
        }

        [Header("Weapons")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        [Header("Viewmodel")]
        [Tooltip("The shared first-person arms. Required for weapons with a WeaponViewmodelSO.")]
        [SerializeField] private ViewmodelRig rig;

        [Header("Stats")]
        [SerializeField] private WeaponStatKeys statKeys;

        [Tooltip("Used when a weapon has no equip_time stat wired.")]
        [SerializeField] private float fallbackEquipTime = 0.5f;
        [Tooltip("Used when a weapon has no stow_time stat wired.")]
        [SerializeField] private float fallbackStowTime = 0.4f;

        [Header("Input")]
        [SerializeField] private Key swapKey = Key.Q;
        [Tooltip("Digit keys select a slot directly. 1 selects slot 0, and so on.")]
        [SerializeField] private bool enableDigitSelect = true;

        [Tooltip("Should match WeaponFireController's reload key. Reloading cancels a " +
                 "sprint the same way firing does. A shared input asset would remove " +
                 "this duplication once the fire controller moves off raw device reads.")]
        [SerializeField] private Key reloadKeyForSprintCancel = Key.R;

        [Header("Sprint")]
        [Tooltip("Optional. When set, the weapon cannot fire while sprinting and needs " +
                 "a moment to come back on target afterwards.")]
        [SerializeField] private PlayerMovement playerMovement;

        [Tooltip("Sprint-exit time as a fraction of this weapon's equip time. Sharing " +
                 "the stat keeps handling meaningful, but coming out of a sprint is a " +
                 "shorter motion than drawing a weapon from nothing.")]
        [Range(0f, 1f)]
        [SerializeField] private float sprintExitFraction = 0.5f;

        [Tooltip("Trying to fire or reload while sprinting drops the sprint and pays " +
                 "the sprint-out first, instead of the input being silently swallowed.")]
        [SerializeField] private bool actionsCancelSprint = true;

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

        private bool wasSprinting;
        private float sprintExitTimer;

        // Fired when a weapon becomes the held one (startup and end of each equip).
        public event Action<WeaponFireController> OnWeaponEquipped;

        // Fired when a transition begins, with its stat-driven duration for clip scaling.
        public event Action<WeaponFireController, float> OnStowStarted;
        public event Action<WeaponFireController, float> OnEquipStarted;
        public event Action<WeaponFireController, float> OnSprintExitStarted;

        public int ActiveIndex => activeIndex;
        public bool IsSwapping => state != SwapState.Ready;

        /// The weapon currently held. Null only if the loadout is empty.
        public WeaponFireController Active =>
            IsValid(activeIndex) ? slots[activeIndex].controller : null;

        public ViewmodelRig Rig => rig;

        private void Awake()
        {
            bus = WeaponEventBus.FindFor(this);

            for (int i = 0; i < slots.Count; i++)
                SpawnViewmodel(i);
        }

        // Spawns the gun model onto the hand socket and uses it as the slot's modelRoot.
        private void SpawnViewmodel(int index)
        {
            if (!IsValid(index)) return;
            var slot = slots[index];

            var source = slot.controller.DamageSource;
            var vm = source != null && source.Weapon != null ? source.Weapon.viewmodel : null;
            if (vm == null) return;

            if (rig == null)
            {
                Debug.LogError($"[Loadout] Slot {index} has a viewmodel but no ViewmodelRig is assigned.");
                return;
            }

            slot.armsController = vm.armsController;

            if (vm.gunPrefab != null)
            {
                slot.gunInstance = Instantiate(vm.gunPrefab, rig.GunSocket, false);
                ApplySkin(slot.gunInstance, source.Weapon.viewmodelSkin);
                if (source.Weapon.defaultShader != null) source.Weapon.defaultShader.ApplyTo(slot.gunInstance);
                slot.gunAnimator = slot.gunInstance.GetComponentInChildren<Animator>(true);
                slot.modelRoot = slot.gunInstance.transform;

                var model = slot.gunInstance.GetComponent<GunModel>();
                if (model != null) slot.sight = model.Sight;
                if (model != null && model.Muzzle != null)
                    slot.controller.SetMuzzle(model.Muzzle);
                else
                    Debug.LogWarning($"[Loadout] Gun prefab '{vm.gunPrefab.name}' has no GunModel muzzle.");
            }
        }

        // Replaces matching materials on every renderer of the spawned gun.
        private static void ApplySkin(GameObject gun, List<MaterialSwap> skin)
        {
            if (gun == null || skin == null || skin.Count == 0) return;

            var renderers = gun.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var mats = renderers[r].sharedMaterials;
                bool changed = false;

                for (int m = 0; m < mats.Length; m++)
                {
                    for (int s = 0; s < skin.Count; s++)
                    {
                        var swap = skin[s];
                        if (swap.from == null || swap.to == null || mats[m] != swap.from) continue;
                        mats[m] = swap.to;
                        changed = true;
                        break;
                    }
                }

                if (changed) renderers[r].sharedMaterials = mats;
            }
        }

        public Animator GunAnimatorOf(WeaponFireController controller)
        {
            for (int i = 0; i < slots.Count; i++)
                if (IsValid(i) && slots[i].controller == controller)
                    return slots[i].gunAnimator;
            return null;
        }

        private void Start()
        {
            for (int i = 0; i < slots.Count; i++)
                CacheVisuals(i);

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
                Debug.LogWarning("[Loadout] Fewer than 2 slots -- swapping is disabled.");

            // All slots start stowed except slot 0; in Start so weapon containers exist first.
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
            TickSprint();
            ApplyReadiness();
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

        // ------------------------------------------------------------------ sprint

        // Sprint exit re-raises the weapon, so it reuses equip_time.
        private void TickSprint()
        {
            if (sprintExitTimer > 0f) sprintExitTimer -= Time.deltaTime;

            bool sprinting = playerMovement != null && playerMovement.IsSprinting;

            // Fire/reload input cancels the sprint instead of being ignored.
            if (actionsCancelSprint && sprinting)
            {
                bool wantsFire = Mouse.current != null && Mouse.current.leftButton.isPressed;
                bool wantsReload = Keyboard.current != null
                                   && Keyboard.current[reloadKeyForSprintCancel].wasPressedThisFrame;

                if (wantsFire || wantsReload)
                {
                    playerMovement.CancelSprint();
                    sprinting = false;
                }
            }

            // Starting a sprint cancels a reload; keyed on resolved sprint, not the key.
            if (!wasSprinting && sprinting)
                AmmoOf(activeIndex)?.CancelReload();

            if (wasSprinting && !sprinting)
            {
                float d = EquipTime(activeIndex) * sprintExitFraction;
                if (d > 0f)
                {
                    sprintExitTimer = d;
                    OnSprintExitStarted?.Invoke(Active, d);
                }
            }

            wasSprinting = sprinting;
        }

        // Single place that decides what the held weapon may do.
        private void ApplyReadiness()
        {
            if (!IsValid(activeIndex)) return;

            bool sprinting = playerMovement != null && playerMovement.IsSprinting;

            // In hand once the swap finishes and you aren't sprinting.
            bool inHand = state == SwapState.Ready && !sprinting;

            // On target only once you've also paid the sprint-out.
            bool canFire = inHand && sprintExitTimer <= 0f;

            SetActive(activeIndex, inHand, canFire);
        }

        // ------------------------------------------------------------------ swap

        // Swap target is the last held weapon, or the next valid slot at startup.
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
                    // Re-selecting the weapon being stowed reverses the swap, carrying progress.
                    if (targetIndex == activeIndex)
                    {
                        float stowProgress = Progress();
                        state = SwapState.Equipping;
                        duration = EquipTime(activeIndex);
                        timer = (1f - stowProgress) * duration;
                        pendingIndex = -1;
                        OnEquipStarted?.Invoke(Active, duration - timer);
                    }
                    else
                    {
                        // Redirect mid-stow: same animation, different destination.
                        pendingIndex = targetIndex;
                    }
                    break;

                case SwapState.Equipping:
                    // Already raising: stow again toward the new target, carrying progress.
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

            OnStowStarted?.Invoke(Active, duration);

            // Gate input the instant the swap starts -- the weapon is on its way down.
            SetActive(activeIndex, false, false);

            // One-off reload cancel on stow; stowed weapons can still reload.
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

                // After ApplyHeldState: its Rebind clears triggers, so Equip must fire after.
                OnEquipStarted?.Invoke(Active, duration);
                return;
            }

            // Equipping finished
            state = SwapState.Ready;
            timer = 0f;
            SetActive(activeIndex, true, true);
            PublishFor(activeIndex, WeaponEventType.Equip);
            OnWeaponEquipped?.Invoke(Active);

            if (debugLog) Debug.Log($"[Loadout] READY {activeIndex}");
        }

        private float Progress() => duration > 0f ? Mathf.Clamp01(timer / duration) : 1f;

        // ------------------------------------------------------------------ state

        // held: model visible and in hand. ready: may accept input.
        private void ApplyHeldState(int index, bool held, bool ready)
        {
            if (!IsValid(index)) return;
            var slot = slots[index];

            // Arms swap first: rebinding clears triggers, so Equip must be set after.
            if (held && rig != null && slot.armsController != null)
                rig.SetWeapon(slot.controller, slot.armsController, slot.sight);

            SetVisible(slot, held);
            SetActive(index, ready, ready);

            var ammo = AmmoOf(index);
            if (ammo != null) ammo.IsHeld = held;
        }

        private void CacheVisuals(int index)
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

            // Rebind on show so a weapon stowed mid-reload doesn't reappear frozen in that pose.
            slot.animator.enabled = true;
            slot.animator.Rebind();
            slot.animator.Update(0f);
        }

        private void SetActive(int index, bool active, bool canFire)
        {
            if (!IsValid(index)) return;
            var c = slots[index].controller;
            if (c == null) return;
            c.IsActive = active;
            c.CanFire = canFire && active;
        }

        // ------------------------------------------------------------------ stats

        private float EquipTime(int index) =>
            ResolveTime(index, statKeys != null ? statKeys.equipTime : null, fallbackEquipTime);

        private float StowTime(int index) =>
            ResolveTime(index, statKeys != null ? statKeys.stowTime : null, fallbackStowTime);

        // Resolved off the weapon's container so perks and armour compose; falls back if unwired.
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