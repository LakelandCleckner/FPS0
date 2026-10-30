using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Events;
using Combat.Loadout;
using Combat.Sources;
using Combat.Stats;

namespace Combat.Weapons
{
    // Presents PlayerLoadout's weapons: spawns, shows and swaps them; origin of Equip/Stow events.
    // Stowed weapons stay enabled so perks and reloads run; removed ones are deactivated, never destroyed.
    public class WeaponLoadout : MonoBehaviour
    {
        // One spawned weapon, cached per owned instance.
        private sealed class Runtime
        {
            public WeaponInstance instance;
            public GameObject root;
            public WeaponFireController controller;
            public WeaponAmmo ammo;

            public GameObject gunInstance;
            public Animator gunAnimator;
            public RuntimeAnimatorController armsController;
            public Transform sight;

            // Renderers visible at spawn; toggled via enabled so the weapon stays subscribed.
            public Renderer[] renderers;

            // Disabled while stowed so it can't resume mid-reload (component, not GameObject).
            public Animator animator;
        }

        [Header("Model")]
        [Tooltip("Found in parents if empty.")]
        [SerializeField] private PlayerLoadout model;

        [Header("Weapons")]
        [Tooltip("Weapon component stack without a model. One is spawned per owned weapon.")]
        [SerializeField] private GameObject runtimePrefab;
        [Tooltip("Parent for spawned weapons; must sit under this object. Empty = this transform.")]
        [SerializeField] private Transform runtimeParent;

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
        [Tooltip("Digit keys select a slot directly. 1 selects slot 1, and so on.")]
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

        private readonly Runtime[] presented = new Runtime[LoadoutSlots.Count];
        private readonly Dictionary<WeaponInstance, Runtime> cache = new Dictionary<WeaponInstance, Runtime>();
        private Transform staging;
        private bool started;

        // Fired when a weapon becomes the held one (startup and end of each equip); null when empty-handed.
        public event Action<WeaponFireController> OnWeaponEquipped;

        // Fired when a transition begins, with its stat-driven duration for clip scaling.
        public event Action<WeaponFireController, float> OnStowStarted;
        public event Action<WeaponFireController, float> OnEquipStarted;
        public event Action<WeaponFireController, float> OnSprintExitStarted;

        // A weapon entered or left the loadout (slot, source); modifier registration hooks here.
        public event Action<int, WeaponDamageSource> WeaponBound;
        public event Action<int, WeaponDamageSource> WeaponUnbound;

        public int ActiveIndex => activeIndex;
        public bool IsSwapping => state != SwapState.Ready;

        /// The weapon currently held. Null when empty-handed.
        public WeaponFireController Active =>
            IsValid(activeIndex) ? presented[activeIndex].controller : null;

        public ViewmodelRig Rig => rig;
        public PlayerLoadout Model => model;

        public WeaponFireController ControllerIn(int slot) =>
            IsValid(slot) ? presented[slot].controller : null;

        private void Awake()
        {
            bus = WeaponEventBus.FindFor(this);
            if (model == null) model = GetComponentInParent<PlayerLoadout>();

            // Spawned weapons wait here so Initialize runs before their Awake.
            var go = new GameObject("WeaponStaging");
            go.SetActive(false);
            staging = go.transform;
            staging.SetParent(transform, false);
        }

        private void OnEnable()
        {
            if (model != null) model.WeaponSlotChanged += HandleSlotChanged;
        }

        private void OnDisable()
        {
            if (model != null) model.WeaponSlotChanged -= HandleSlotChanged;
        }

        // In Start so every scene Awake (stat registry, bus, combatant) has run first.
        private void Start()
        {
            if (model == null)
            {
                Debug.LogError("[Loadout] No PlayerLoadout found.");
                return;
            }

            model.EnsureInitialized();

            for (int i = 0; i < presented.Length; i++)
                Bind(i, model.GetWeapon(i));

            activeIndex = FirstValid();
            if (activeIndex < 0) activeIndex = 0;
            previousIndex = activeIndex;

            // All slots start stowed except the first filled one, which starts ready.
            for (int i = 0; i < presented.Length; i++)
                ApplyHeldState(i, held: i == activeIndex, ready: i == activeIndex);

            started = true;

            if (Active != null) PublishFor(activeIndex, WeaponEventType.Equip);
            OnWeaponEquipped?.Invoke(Active);
        }

        private void Update()
        {
            ReadInput();
            TickSwap();
            TickSprint();
            ApplyReadiness();
        }

        // ------------------------------------------------------------------ spawning

        // Returns the cached runtime for an owned weapon, spawning it on first use.
        private Runtime Acquire(WeaponInstance instance)
        {
            if (instance == null || instance.Definition == null) return null;

            if (cache.TryGetValue(instance, out var cached))
            {
                if (!cached.root.activeSelf) cached.root.SetActive(true);
                return cached;
            }

            if (runtimePrefab == null)
            {
                Debug.LogError("[Loadout] No runtime prefab assigned.");
                return null;
            }

            var go = Instantiate(runtimePrefab, staging, false);
            go.name = "Weapon_" + instance.Definition.name;

            var controller = go.GetComponentInChildren<WeaponFireController>(true);
            var source = controller != null ? controller.DamageSource : null;
            if (source == null)
            {
                Debug.LogError("[Loadout] Runtime prefab needs a WeaponFireController with a WeaponDamageSource.");
                Destroy(go);
                return null;
            }

            source.Initialize(instance);

            var rt = new Runtime
            {
                instance = instance,
                root = go,
                controller = controller,
                ammo = controller.GetComponent<WeaponAmmo>()
            };

            SpawnViewmodel(rt);
            CacheVisuals(rt);
            cache[instance] = rt;

            // Leaving staging runs Awake/OnEnable with the instance already injected.
            go.transform.SetParent(runtimeParent != null ? runtimeParent : transform, false);

            if (debugLog) Debug.Log($"[Loadout] Spawned {go.name}");
            return rt;
        }

        // Spawns the gun model onto the hand socket and uses it as the runtime's model root.
        private void SpawnViewmodel(Runtime rt)
        {
            var weapon = rt.controller.DamageSource.Weapon;
            var vm = weapon != null ? weapon.viewmodel : null;
            if (vm == null) return;

            if (rig == null)
            {
                Debug.LogError($"[Loadout] '{weapon.name}' has a viewmodel but no ViewmodelRig is assigned.");
                return;
            }

            rt.armsController = vm.armsController;

            if (vm.gunPrefab == null) return;

            rt.gunInstance = Instantiate(vm.gunPrefab, rig.GunSocket, false);
            ApplySkin(rt.gunInstance, weapon.viewmodelSkin);
            if (weapon.defaultShader != null) weapon.defaultShader.ApplyTo(rt.gunInstance);
            rt.gunAnimator = rt.gunInstance.GetComponentInChildren<Animator>(true);

            var gunModel = rt.gunInstance.GetComponent<GunModel>();
            if (gunModel != null) rt.sight = gunModel.Sight;
            if (gunModel != null && gunModel.Muzzle != null)
                rt.controller.SetMuzzle(gunModel.Muzzle);
            else
                Debug.LogWarning($"[Loadout] Gun prefab '{vm.gunPrefab.name}' has no GunModel muzzle.");
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

        private void CacheVisuals(Runtime rt)
        {
            var root = rt.gunInstance != null ? rt.gunInstance.transform : rt.controller.transform;

            var all = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            var visible = new List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
                if (all[i].enabled) visible.Add(all[i]);

            rt.renderers = visible.ToArray();
            rt.animator = root.GetComponentInChildren<Animator>(includeInactive: true);
        }

        public Animator GunAnimatorOf(WeaponFireController controller)
        {
            foreach (var rt in cache.Values)
                if (rt.controller == controller) return rt.gunAnimator;
            return null;
        }

        // ------------------------------------------------------------------ binding

        // Puts an owned weapon into a slot, hidden. Never touches the hand.
        private void Bind(int slot, WeaponInstance instance)
        {
            var rt = Acquire(instance);
            presented[slot] = rt;
            if (rt == null) return;

            ApplyHeldState(slot, held: false, ready: false);
            WeaponBound?.Invoke(slot, rt.controller.DamageSource);

            if (debugLog) Debug.Log($"[Loadout] BIND slot {slot} -> {rt.root.name}");
        }

        // Takes a weapon out of the loadout and deactivates it.
        private void Unbind(int slot)
        {
            var rt = presented[slot];
            if (rt == null) return;

            ApplyHeldState(slot, held: false, ready: false);
            if (rt.ammo != null) rt.ammo.CancelReload();

            presented[slot] = null;
            WeaponUnbound?.Invoke(slot, rt.controller.DamageSource);
            rt.root.SetActive(false);

            if (debugLog) Debug.Log($"[Loadout] UNBIND slot {slot} <- {rt.root.name}");
        }

        private bool NeedsSync(int slot)
        {
            var want = model != null ? model.GetWeapon(slot) : null;
            var have = presented[slot];
            return (have != null ? have.instance : null) != want;
        }

        private int SlotPresenting(Runtime rt)
        {
            if (rt == null) return -1;
            for (int i = 0; i < presented.Length; i++)
                if (presented[i] == rt) return i;
            return -1;
        }

        private int SlotPresenting(WeaponInstance instance)
        {
            if (instance == null) return -1;
            for (int i = 0; i < presented.Length; i++)
                if (presented[i] != null && presented[i].instance == instance) return i;
            return -1;
        }

        // Syncs every slot but `skip`; a weapon still shown elsewhere waits for that slot to release it.
        private void SyncSlots(int skip)
        {
            bool changed = true;
            for (int pass = 0; changed && pass < presented.Length; pass++)
            {
                changed = false;
                for (int i = 0; i < presented.Length; i++)
                {
                    if (i == skip || !NeedsSync(i)) continue;

                    var want = model.GetWeapon(i);
                    int shownIn = SlotPresenting(want);
                    if (shownIn >= 0 && shownIn != i && NeedsSync(shownIn) && shownIn != skip)
                        Unbind(shownIn);
                    else if (shownIn >= 0 && shownIn != i)
                        continue;

                    Unbind(i);
                    Bind(i, want);
                    changed = true;
                }
            }
        }

        private void HandleSlotChanged(int slot, WeaponInstance oldWeapon, WeaponInstance newWeapon)
        {
            if (!started || slot < 0 || slot >= presented.Length) return;

            bool holding = IsValid(activeIndex);

            // In hand: put it away first; TickSwap swaps the contents once it's down.
            if (holding && slot == activeIndex)
            {
                if (NeedsSync(slot)) ReplaceHeld();
                return;
            }

            SyncSlots(holding ? activeIndex : -1);

            // Empty-handed: bring the new weapon straight up.
            if (!IsValid(activeIndex) && state == SwapState.Ready && IsValid(slot))
                BeginEquip(slot);
        }

        private void ReplaceHeld()
        {
            switch (state)
            {
                case SwapState.Ready:
                    BeginStow(activeIndex);
                    break;

                case SwapState.Equipping:
                    {
                        // Reverse the raise, carrying progress.
                        float equipProgress = Progress();
                        BeginStow(activeIndex);
                        timer = (1f - equipProgress) * duration;
                        break;
                    }

                    // Stowing: already on its way down; TickSwap syncs it.
            }
        }

        // ------------------------------------------------------------------ input

        private void ReadInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[swapKey].wasPressedThisFrame)
                RequestSwap(SwapTarget());

            if (!enableDigitSelect) return;

            if (kb.digit1Key.wasPressedThisFrame) RequestSwap(0);
            else if (kb.digit2Key.wasPressedThisFrame) RequestSwap(1);
            else if (kb.digit3Key.wasPressedThisFrame) RequestSwap(2);
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
            {
                var ammo = AmmoOf(activeIndex);
                if (ammo != null) ammo.CancelReload();
            }

            if (wasSprinting && !sprinting && IsValid(activeIndex))
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

        // Swap target is the last held weapon, or the next filled slot.
        private int SwapTarget()
        {
            if (previousIndex != activeIndex && IsValid(previousIndex))
                return previousIndex;

            for (int i = 1; i <= presented.Length; i++)
            {
                int idx = (activeIndex + i) % presented.Length;
                if (IsValid(idx)) return idx;
            }
            return activeIndex;
        }

        public void RequestSwap(int targetIndex)
        {
            if (debugLog)
                Debug.Log($"[Loadout] RequestSwap target={targetIndex} active={activeIndex} " +
                          $"prev={previousIndex} state={state}");

            if (!IsValid(targetIndex))
            {
                if (debugLog) Debug.Log($"[Loadout]   rejected: slot {targetIndex} empty");
                return;
            }

            // Empty-handed: just raise the target.
            if (!IsValid(activeIndex))
            {
                if (state == SwapState.Ready) BeginEquip(targetIndex);
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
                    if (targetIndex == activeIndex)
                    {
                        // Contents are being replaced: keep stowing and come back up with the new one.
                        if (NeedsSync(activeIndex))
                        {
                            pendingIndex = activeIndex;
                            return;
                        }

                        // Re-selecting the weapon being stowed reverses the swap, carrying progress.
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
            var ammo = AmmoOf(activeIndex);
            if (ammo != null) ammo.CancelReload();
        }

        // Raises a weapon with nothing in hand.
        private void BeginEquip(int slot)
        {
            activeIndex = slot;
            ApplyHeldState(slot, held: true, ready: false);

            state = SwapState.Equipping;
            duration = EquipTime(slot);
            timer = 0f;

            if (debugLog) Debug.Log($"[Loadout] EQUIP {slot} from empty over {duration:F2}s");

            // After ApplyHeldState: its Rebind clears triggers, so Equip must fire after.
            OnEquipStarted?.Invoke(Active, duration);
        }

        private void TickSwap()
        {
            if (state == SwapState.Ready) return;

            timer += Time.deltaTime;
            if (timer < duration) return;

            if (state == SwapState.Stowing)
            {
                int outgoing = activeIndex;
                var outgoingRt = presented[outgoing];

                ApplyHeldState(outgoing, held: false, ready: false);
                PublishFor(outgoing, WeaponEventType.Stow);

                // Contents changed while it was in hand: swap now that it's down.
                SyncSlots(-1);

                int incoming = IsValid(pendingIndex) ? pendingIndex : FallbackFrom(outgoing, outgoingRt);
                pendingIndex = -1;

                if (incoming < 0)
                {
                    // Nothing left to hold.
                    state = SwapState.Ready;
                    timer = 0f;
                    previousIndex = outgoing;
                    OnWeaponEquipped?.Invoke(null);
                    if (debugLog) Debug.Log("[Loadout] EMPTY-HANDED");
                    return;
                }

                // A same-slot replacement keeps the real previous weapon for the swap key.
                if (incoming != outgoing) previousIndex = outgoing;
                activeIndex = incoming;

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

        // Stow target vanished: back up, follow the weapon to its new slot, the previous weapon, or any filled slot.
        private int FallbackFrom(int outgoing, Runtime outgoingRt)
        {
            if (IsValid(outgoing)) return outgoing;
            int moved = SlotPresenting(outgoingRt);
            if (moved >= 0) return moved;
            if (IsValid(previousIndex)) return previousIndex;
            return FirstValid();
        }

        private int FirstValid()
        {
            for (int i = 0; i < presented.Length; i++)
                if (IsValid(i)) return i;
            return -1;
        }

        private float Progress() => duration > 0f ? Mathf.Clamp01(timer / duration) : 1f;

        // ------------------------------------------------------------------ state

        // held: model visible and in hand. ready: may accept input.
        private void ApplyHeldState(int index, bool held, bool ready)
        {
            if (!IsValid(index)) return;
            var rt = presented[index];

            // Arms swap first: rebinding clears triggers, so Equip must be set after.
            if (held && rig != null && rt.armsController != null)
                rig.SetWeapon(rt.controller, rt.armsController, rt.sight);

            SetVisible(rt, held);
            SetActive(index, ready, ready);

            if (rt.ammo != null) rt.ammo.IsHeld = held;
        }

        private static void SetVisible(Runtime rt, bool visible)
        {
            if (rt.renderers != null)
            {
                for (int i = 0; i < rt.renderers.Length; i++)
                {
                    var r = rt.renderers[i];
                    if (r != null) r.enabled = visible;
                }
            }

            if (rt.animator == null) return;

            if (!visible)
            {
                rt.animator.enabled = false;
                return;
            }

            // Rebind on show so a weapon stowed mid-reload doesn't reappear frozen in that pose.
            rt.animator.enabled = true;
            rt.animator.Rebind();
            rt.animator.Update(0f);
        }

        private void SetActive(int index, bool active, bool canFire)
        {
            if (!IsValid(index)) return;
            var c = presented[index].controller;
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

            var source = presented[index].controller.DamageSource;
            var container = source != null ? source.SourceStats : null;
            if (container == null) return fallback;

            float v = container.Resolve(def);
            return v > 0f ? v : fallback;
        }

        // ------------------------------------------------------------------ misc

        private bool IsValid(int i) => i >= 0 && i < presented.Length && presented[i] != null
                                       && presented[i].controller != null;

        private WeaponAmmo AmmoOf(int index) => IsValid(index) ? presented[index].ammo : null;

        private void PublishFor(int index, WeaponEventType type)
        {
            if (bus == null || !IsValid(index)) return;
            var source = presented[index].controller.DamageSource;
            if (source == null) return;
            bus.Publish(WeaponEvent.ForWeapon(type, source));
        }
    }
}
