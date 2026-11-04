using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Combat.Armor;
using Combat.Loadout;
using Combat.Stats;
using Combat.Weapons;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Combat.UI
{
    public enum InventoryTab { Loadout, Character }

    // The inventory menu. Opens on Menu/Inventory, edits the live loadout, and previews in edit mode.
    [ExecuteAlways]
    public class InventoryScreen : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField] private UIStyleSO style;
        [SerializeField] private StatSheetSO weaponStatSheet;
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

        [Header("Behaviour")]
        [SerializeField] private float messageTime = 2.5f;

        [Header("Edit-mode preview")]
        [Tooltip("Fill the panels in edit mode from the loadout below.")]
        [SerializeField] private bool preview = true;
        [SerializeField] private StartingLoadoutSO previewLoadout;
        [Tooltip("Armour slots to show in the preview. Empty = the scene PlayerLoadout's slots.")]
        [SerializeField] private List<ArmorSlotSO> previewArmorSlots = new List<ArmorSlotSO>();

        private readonly LoadoutView view = new LoadoutView();
        private readonly WeaponStatPreview statPreview = new WeaponStatPreview();
        private readonly object uiOwner = new object();
        private PlayerInputRouter input;
        private PlayerInventory inventory;
        private bool open, dirty = true, detailDirty, footerDirty, building, previewBuilt, subscribed;
        private float messageUntil;

        public UIStyleSO Style => style;
        public StatSheetSO WeaponStatSheet => weaponStatSheet;
        public LoadoutView View => view;
        public InventoryTab Tab { get; private set; } = InventoryTab.Loadout;
        public SlotRef Selected { get; private set; } = new SlotRef(SlotKind.Weapon, 0);
        public object Hovered { get; private set; }
        public string Message { get; private set; } = "";
        public bool IsOpen => open;
        public PlayerInputRouter Input => input;
        public bool IsLive => Application.isPlaying && !view.IsPreview;

        // The item the detail panel describes: hovered, else what's in the selected slot.
        public object Focus => Hovered ?? view.Equipped(Selected);

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
            Subscribe(true);
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                Subscribe(false);
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
            if (on)
            {
                loadout.WeaponSlotChanged += HandleWeaponSlot;
                loadout.ArmorSlotChanged += HandleArmorSlot;
                loadout.LockChanged += HandleLock;
                if (inventory != null) inventory.Changed += MarkDirty;
            }
            else
            {
                loadout.WeaponSlotChanged -= HandleWeaponSlot;
                loadout.ArmorSlotChanged -= HandleArmorSlot;
                loadout.LockChanged -= HandleLock;
                if (inventory != null) inventory.Changed -= MarkDirty;
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
            if (input != null)
            {
                if (input.Menu.Inventory.WasPressedThisFrame()) SetOpen(!open);
                else if (open && input.UI.Cancel.WasPressedThisFrame()) SetOpen(false);
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
                Hovered = null;
                Message = "";
            }
        }

        // ------------------------------------------------------------------ actions

        public void SetTab(InventoryTab tab)
        {
            if (Tab == tab) return;
            Tab = tab;
            Hovered = null;
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

        // Stat layers for a weapon, with the current equipped armour.
        public WeaponStatPreview PreviewFor(WeaponInstance weapon)
        {
            if (!Application.isPlaying) StatBootstrap.EnsureRegistry();
            statPreview.Build(weapon, view.EquippedArmor(), statKeys);
            return statPreview;
        }

        // ------------------------------------------------------------------ building

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

            if (backdrop != null) backdrop.color = style.backdrop;
            if (loadoutTab != null) loadoutTab.SetActive(Tab == InventoryTab.Loadout);
            if (characterTab != null) characterTab.SetActive(Tab == InventoryTab.Character);

            Build(header);
            if (Tab == InventoryTab.Loadout)
            {
                Build(slotsPanel);
                Build(carriedPanel);
                Build(detailPanel);
            }
            Build(footer);
        }

        private void RebuildDetail()
        {
            detailDirty = false;
            if (Tab == InventoryTab.Loadout) Build(detailPanel);
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
            foreach (var p in new InventoryPanel[] { header, slotsPanel, carriedPanel, detailPanel, footer })
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
