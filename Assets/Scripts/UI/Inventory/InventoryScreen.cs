using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Combat.Armor;
using Combat.Loadout;
using Combat.Perks;
using Combat.Stats;
using Combat.Weapons;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Combat.UI
{
    public enum InventoryTab { Loadout, Character, Vault }

    // The inventory menu. Opens on Menu/Inventory, edits the live loadout, and previews in edit mode.
    [ExecuteAlways]
    public class InventoryScreen : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField] private UIStyleSO style;
        [SerializeField] private StatSheetSO weaponStatSheet;
        [SerializeField] private StatSheetSO characterStatSheet;
        [SerializeField] private WeaponStatKeys statKeys;

        [Header("Source")]
        [Tooltip("Found on the local player if empty.")]
        [SerializeField] private PlayerLoadout loadout;

        [Header("Parts")]
        [Tooltip("Shown while open. Kept visible in edit mode so the layout can be arranged.")]
        [SerializeField] private GameObject content;
        [SerializeField] private Image backdrop;
        [SerializeField] private GameObject loadoutTab;
        [SerializeField] private GameObject characterTab;
        [SerializeField] private InventoryHeader header;
        [SerializeField] private LoadoutSlotsPanel slotsPanel;
        [SerializeField] private CarriedPanel carriedPanel;
        [SerializeField] private ItemDetailPanel detailPanel;
        [SerializeField] private InventoryFooter footer;
        [SerializeField] private CharacterPanel characterPanel;
        [SerializeField] private StatTooltip tooltip;

        [Header("Vault tab")]
        [SerializeField] private GameObject vaultTab;
        [SerializeField] private VaultFilterBar filterBar;
        [SerializeField] private ItemGridPanel vaultCarriedPanel;
        [SerializeField] private ItemGridPanel vaultPanel;
        [SerializeField] private ItemDetailPanel vaultDetailPanel;

        [Header("Behaviour")]
        [SerializeField] private float messageTime = 2.5f;
        [Tooltip("Seconds to hold delete on an item.")]
        [Min(0.1f)] [SerializeField] private float deleteHoldTime = 1f;
        [Tooltip("Seconds to hold delete on items of Long Hold From rarity and up.")]
        [Min(0.1f)] [SerializeField] private float longDeleteHoldTime = 2f;
        [SerializeField] private Rarity longHoldFrom = Rarity.Legendary;

        [Header("Edit-mode preview")]
        [Tooltip("Fill the panels in edit mode from the loadout below.")]
        [SerializeField] private bool preview = true;
        [Tooltip("Tab shown in the edit-mode preview.")]
        [SerializeField] private InventoryTab previewTab = InventoryTab.Loadout;
        [SerializeField] private StartingLoadoutSO previewLoadout;
        [Tooltip("Armor slots to show in the preview. Empty = the scene PlayerLoadout's slots.")]
        [SerializeField] private List<ArmorSlotSO> previewArmorSlots = new List<ArmorSlotSO>();

        private readonly LoadoutView view = new LoadoutView();
        private readonly WeaponStatPreview statPreview = new WeaponStatPreview();
        private readonly object uiOwner = new object();
        private PlayerInputRouter input;
        private PlayerInventory inventory;
        private PlayerVault vault;
        private object holdItem;
        private float held;
        private bool waitRelease;
        private bool open, dirty = true, detailDirty, footerDirty, building, previewBuilt, subscribed;
        private float messageUntil;

        public UIStyleSO Style => style;
        public StatSheetSO WeaponStatSheet => weaponStatSheet;
        public StatSheetSO CharacterStatSheet => characterStatSheet;
        public StatTooltip Tooltip => tooltip;
        public CombatantStats PlayerStats { get; private set; }
        public CombatantHealth PlayerHealth { get; private set; }
        public LoadoutView View => view;
        public InventoryTab Tab { get; private set; } = InventoryTab.Loadout;
        public SlotRef Selected { get; private set; } = new SlotRef(SlotKind.Weapon, 0);
        public object Hovered { get; private set; }
        public string Message { get; private set; } = "";
        public bool IsOpen => open;
        public PlayerInputRouter Input => input;
        public bool IsLive => Application.isPlaying && !view.IsPreview;
        public ItemFilter Filter { get; } = new ItemFilter();

        // The item the detail panel describes: hovered, else (loadout tab) what's in the selected slot.
        public object Focus => Hovered ?? (Tab == InventoryTab.Loadout ? view.Equipped(Selected) : null);

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (Application.isPlaying && content != null) content.SetActive(false);
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            input = PlayerInputRouter.Local;
            if (loadout == null && input != null) loadout = input.GetComponentInParent<PlayerLoadout>();
            if (loadout == null) loadout = FindFirstObjectByType<PlayerLoadout>();
            if (loadout == null) Debug.LogWarning("[Inventory] No PlayerLoadout found.");
            PlayerStats = loadout != null ? loadout.GetComponentInParent<CombatantStats>() : null;
            PlayerHealth = PlayerStats != null ? PlayerStats.GetComponent<CombatantHealth>() : null;
            Subscribe(true);
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                Subscribe(false);
                if (tooltip != null) tooltip.Hide();
                if (open && input != null) input.PopUI(uiOwner);
                open = false;
            }
        }

        private void Subscribe(bool on)
        {
            if (loadout == null || subscribed == on) return;
            subscribed = on;
            loadout.EnsureInitialized();
            inventory = loadout.Inventory;
            vault = inventory != null ? inventory.GetComponent<PlayerVault>() : null;
            if (on)
            {
                loadout.WeaponSlotChanged += HandleWeaponSlot;
                loadout.ArmorSlotChanged += HandleArmorSlot;
                loadout.LockChanged += HandleLock;
                if (inventory != null) inventory.Changed += MarkDirty;
                if (vault != null) vault.Changed += MarkDirty;
            }
            else
            {
                loadout.WeaponSlotChanged -= HandleWeaponSlot;
                loadout.ArmorSlotChanged -= HandleArmorSlot;
                loadout.LockChanged -= HandleLock;
                if (inventory != null) inventory.Changed -= MarkDirty;
                if (vault != null) vault.Changed -= MarkDirty;
            }
        }

        private void HandleWeaponSlot(int slot, WeaponInstance a, WeaponInstance b) => MarkDirty();
        private void HandleArmorSlot(ArmorSlotSO slot, ArmorInstance a, ArmorInstance b) => MarkDirty();
        private void HandleLock(bool locked) => MarkDirty();
        private void MarkDirty() => dirty = true;

        private void Update()
        {
            if (!Application.isPlaying) return;

            if (input == null) input = PlayerInputRouter.Local;
            bool typing = open && filterBar != null && filterBar.SearchFocused;
            if (input != null)
            {
                // While typing in search, keys go to the text; Cancel just leaves the field.
                if (typing)
                {
                    if (input.UI.Cancel.WasPressedThisFrame()) filterBar.Unfocus();
                }
                else if (input.Menu.Inventory.WasPressedThisFrame()) SetOpen(!open);
                else if (open && input.UI.Cancel.WasPressedThisFrame()) SetOpen(false);

                if (open && !typing) ItemKeys();
                else ResetHold();
            }

            if (open && Message.Length > 0 && Time.unscaledTime >= messageUntil)
            {
                Message = "";
                footerDirty = true;
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !open) return;
            if (dirty) RebuildAll();
            else
            {
                if (detailDirty) RebuildDetail();
                if (footerDirty) RebuildFooter();
            }
            if (Tab == InventoryTab.Character && characterPanel != null) characterPanel.Tick(this);
        }

        public void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            if (content != null) content.SetActive(open);

            if (open)
            {
                if (input != null) input.PushUI(uiOwner);
                Hovered = null;
                RebuildAll();
            }
            else
            {
                if (input != null) input.PopUI(uiOwner);
                if (tooltip != null) tooltip.Hide();
                if (filterBar != null) filterBar.Unfocus();
                Hovered = null;
                Message = "";
                ResetHold();
            }
        }

        // ------------------------------------------------------------------ store and delete

        // Store moves the hovered item between carried and vault; holding Delete removes it.
        private void ItemKeys()
        {
            var item = Hovered;
            if (input.UI.Store.WasPressedThisFrame() && item != null)
            {
                if (view.IsCarried(item)) MoveToVault(item);
                else if (view.IsVaulted(item)) MoveToCarried(item);
            }

            if (input.UI.Delete.WasPressedThisFrame() && item != null && !CanDelete(item) && IsEquipped(item))
                Say("Unequip it before deleting.");

            // After a delete, the key must be let go before the next item can be held.
            if (waitRelease)
            {
                if (!input.UI.Delete.IsPressed()) waitRelease = false;
                ResetHold();
                return;
            }

            if (item == null || !CanDelete(item) || !input.UI.Delete.IsPressed()) { ResetHold(); return; }
            if (holdItem != item) { holdItem = item; held = 0f; }
            held += Time.unscaledDeltaTime;
            if (held >= HoldTime(item)) Delete(item);
        }

        private void ResetHold()
        {
            holdItem = null;
            held = 0f;
        }

        private float HoldTime(object item)
        {
            var r = ItemText.RarityOf(item);
            return r.HasValue && r.Value >= longHoldFrom ? longDeleteHoldTime : deleteHoldTime;
        }

        // 0..1 for the item being held, else 0.
        public float DeleteProgress(object item)
            => item != null && item == holdItem ? Mathf.Clamp01(held / HoldTime(item)) : 0f;

        public bool CanDelete(object item) => IsLive && (view.IsCarried(item) || view.IsVaulted(item));

        private bool IsEquipped(object item)
            => item is WeaponInstance w ? System.Array.IndexOf(view.Weapons, w) >= 0 : item is ArmorInstance a && view.Armor.Contains(a);

        public void MoveToVault(object item)
        {
            if (!IsLive || inventory == null) return;
            if (vault == null) { Say("No vault on the player."); return; }

            if (item is WeaponInstance w && inventory.RemoveWeapon(w)) vault.TryAddWeapon(w);
            else if (item is ArmorInstance a && inventory.RemoveArmor(a)) vault.TryAddArmor(a);
            else return;
            Hovered = null;
        }

        public void MoveToCarried(object item)
        {
            if (!IsLive || inventory == null || vault == null) return;

            if (item is WeaponInstance w)
            {
                if (!inventory.HasWeaponSpaceFor(w)) { Say("Carried slot " + (w.Definition.InventorySlot + 1) + " is full."); return; }
                if (vault.RemoveWeapon(w)) inventory.TryAddWeapon(w);
            }
            else if (item is ArmorInstance a)
            {
                if (!inventory.HasArmorSpace(a.Slot)) { Say("Carried " + ItemText.SlotName(a.Slot) + " is full."); return; }
                if (vault.RemoveArmor(a)) inventory.TryAddArmor(a);
            }
            else return;
            Hovered = null;
        }

        public void Delete(object item)
        {
            ResetHold();
            waitRelease = true;
            if (!CanDelete(item)) return;

            bool gone = false;
            if (item is WeaponInstance w) gone = inventory.RemoveWeapon(w) || (vault != null && vault.RemoveWeapon(w));
            else if (item is ArmorInstance a) gone = inventory.RemoveArmor(a) || (vault != null && vault.RemoveArmor(a));
            if (!gone) return;

            Hovered = null;
            Say("Deleted " + ItemText.Name(item));
        }

        // ------------------------------------------------------------------ actions

        public void SetTab(InventoryTab tab)
        {
            if (Tab == tab) return;
            Tab = tab;
            Hovered = null;
            if (tooltip != null) tooltip.Hide();
            Request();
        }

        public void Select(SlotRef slot)
        {
            Selected = slot;
            Hovered = null;
            Request();
        }

        // Right-click on an equipped slot: send it to carried.
        public void Unequip(SlotRef slot)
        {
            if (!IsLive || loadout == null) return;
            string reason;
            bool ok = slot.Kind == SlotKind.Weapon ? loadout.TryClear(slot.Index, out reason) : loadout.TryClearArmor(slot.Index, out reason);
            if (!ok) Say(reason);
        }

        // Left-click on a carried item: equip it into the selected slot.
        public void EquipToSelected(object item)
        {
            if (!IsLive || loadout == null) return;
            string reason = null;
            bool ok = false;
            if (item is WeaponInstance w && Selected.Kind == SlotKind.Weapon) ok = loadout.TryEquip(Selected.Index, w, out reason);
            else if (item is ArmorInstance a) ok = loadout.TryEquipArmor(a, out reason);
            if (ok) Hovered = null;
            else Say(reason);
        }

        public void OnTileHover(UITile tile, bool on)
        {
            if (building || tile == null) return;
            if (on) Hovered = tile.Item;
            else if (Hovered == tile.Item) Hovered = null;
            else return;
            detailDirty = true;
            if (!Application.isPlaying) Request();
        }

        public void Say(string message)
        {
            Message = string.IsNullOrEmpty(message) ? "" : message;
            messageUntil = Time.unscaledTime + messageTime;
            footerDirty = true;
        }

        // Stat layers for a weapon, with the current equipped armor.
        public WeaponStatPreview PreviewFor(WeaponInstance weapon)
        {
            if (!Application.isPlaying) StatBootstrap.EnsureRegistry();
            statPreview.Build(weapon, view.EquippedArmor(), statKeys);
            return statPreview;
        }

        // ------------------------------------------------------------------ building

        public void RequestRebuild() => Request();

        private void Request()
        {
            dirty = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) QueueEditorRebuild();
#endif
        }

        private void RebuildAll()
        {
            dirty = detailDirty = footerDirty = false;
            if (style == null) return;

            if (Application.isPlaying) view.FromLoadout(loadout);
            ClampSelection();

            if (tooltip != null && !tooltip.IsPinned) tooltip.Hide();
            if (backdrop != null) backdrop.color = style.backdrop;
            if (loadoutTab != null) loadoutTab.SetActive(Tab == InventoryTab.Loadout);
            if (characterTab != null) characterTab.SetActive(Tab == InventoryTab.Character);
            if (vaultTab != null) vaultTab.SetActive(Tab == InventoryTab.Vault);

            Build(header);
            if (Tab == InventoryTab.Loadout)
            {
                Build(slotsPanel);
                Build(carriedPanel);
                Build(detailPanel);
            }
            else if (Tab == InventoryTab.Vault)
            {
                Build(filterBar);
                Build(vaultCarriedPanel);
                Build(vaultPanel);
                Build(vaultDetailPanel);
            }
            else Build(characterPanel);
            Build(footer);
        }

        private void RebuildDetail()
        {
            detailDirty = false;
            if (Tab == InventoryTab.Loadout) Build(detailPanel);
            else if (Tab == InventoryTab.Vault) Build(vaultDetailPanel);
        }

        private void RebuildFooter()
        {
            footerDirty = false;
            Build(footer);
        }

        private void Build(InventoryPanel panel)
        {
            if (panel == null) return;
            building = true;
            UIKit.Transient = !Application.isPlaying;
            try { panel.Rebuild(this); }
            finally
            {
                UIKit.Transient = false;
                building = false;
            }
        }

        private void ClampSelection()
        {
            int max = Selected.Kind == SlotKind.Weapon ? view.Weapons.Length : view.ArmorSlots.Count;
            if (max <= 0) Selected = new SlotRef(SlotKind.Weapon, 0);
            else if (Selected.Index >= max) Selected = new SlotRef(Selected.Kind, max - 1);
        }

        private void ClearAll()
        {
            foreach (var p in new InventoryPanel[] { header, slotsPanel, carriedPanel, detailPanel, footer, characterPanel, filterBar, vaultCarriedPanel, vaultPanel, vaultDetailPanel })
                if (p != null) p.Clear();
        }

        // ------------------------------------------------------------------ edit-mode preview

#if UNITY_EDITOR
        private void OnEnable()
        {
            if (!Application.isPlaying) QueueEditorRebuild();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) { dirty = true; return; }
            previewBuilt = false;
            QueueEditorRebuild();
        }

        private void QueueEditorRebuild()
        {
            EditorApplication.delayCall -= EditorRebuild;
            EditorApplication.delayCall += EditorRebuild;
        }

        private void EditorRebuild()
        {
            if (this == null || Application.isPlaying || !isActiveAndEnabled) return;

            if (!preview || style == null)
            {
                ClearAll();
                return;
            }

            if (!previewBuilt)
            {
                var slots = previewArmorSlots.Count > 0 ? previewArmorSlots : SceneArmorSlots();
                view.FromStarting(previewLoadout, slots);
                previewBuilt = true;
            }
            Tab = previewTab;
            RebuildAll();
        }

        private static IList<ArmorSlotSO> SceneArmorSlots()
        {
            var l = FindFirstObjectByType<PlayerLoadout>();
            return l != null ? new List<ArmorSlotSO>(l.ArmorSlots) : new List<ArmorSlotSO>();
        }

        [ContextMenu("Reroll Preview")]
        private void RerollPreview()
        {
            previewBuilt = false;
            QueueEditorRebuild();
        }

        [ContextMenu("Clear Generated")]
        private void ClearGenerated() => ClearAll();
#endif
    }
}
