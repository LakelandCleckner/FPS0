using UnityEngine;
using Combat.Armor;
using Combat.Loadout;
using Combat.Perks;
using Combat.UI;
using Combat.Weapons;

namespace Combat.Loot
{
    // A rolled item lying in the world. Walk over it to take it: carried if there's room, else the vault.
    public class ItemDrop : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Header("Look")]
        [Tooltip("Rarity colors come from here.")]
        [SerializeField] private UIStyleSO style;
        [Tooltip("Spins and bobs. Empty = nothing moves.")]
        [SerializeField] private Transform visual;
        [Tooltip("Tinted to the rarity color.")]
        [SerializeField] private Renderer[] tinted = new Renderer[0];
        [SerializeField] private LineRenderer beam;
        [SerializeField] private float beamHeight = 2.5f;
        [Range(0f, 1f)] [SerializeField] private float beamAlpha = 0.6f;
        [SerializeField] private Light glow;

        [Header("Motion")]
        [SerializeField] private float spinSpeed = 90f;
        [SerializeField] private float bobHeight = 0.08f;
        [SerializeField] private float bobSpeed = 2f;

        [Header("Audio")]
        [SerializeField] private AudioClip pickupClip;

        private WeaponInstance weapon;
        private ArmorInstance armor;
        private Vector3 visualBase;
        private float phase;
        private bool taken;

        public WeaponInstance Weapon => weapon;
        public ArmorInstance Armor => armor;
        public Rarity Rarity => weapon != null ? weapon.Rarity : armor != null ? armor.Rarity : Rarity.Common;

        public string Label
        {
            get
            {
                if (weapon != null && weapon.Definition != null) return Name(weapon.Definition.displayName, weapon.Definition.name);
                if (armor != null && armor.Definition != null) return Name(armor.Definition.displayName, armor.Definition.name);
                return "nothing";
            }
        }

        public void Init(WeaponInstance item) { weapon = item; armor = null; Tint(); }
        public void Init(ArmorInstance item) { armor = item; weapon = null; Tint(); }

        private void Awake()
        {
            if (visual != null) visualBase = visual.localPosition;
            phase = Random.value * Mathf.PI * 2f;
        }

        private void Update()
        {
            if (visual == null) return;
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight);
        }

        private void Tint()
        {
            Color c = style != null ? style.RarityColor(Rarity) : Color.white;

            var block = new MaterialPropertyBlock();
            foreach (var r in tinted)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block);
            }

            if (beam != null)
            {
                beam.useWorldSpace = false;
                beam.positionCount = 2;
                beam.SetPosition(0, Vector3.zero);
                beam.SetPosition(1, Vector3.up * beamHeight);
                beam.startColor = new Color(c.r, c.g, c.b, beamAlpha);
                beam.endColor = new Color(c.r, c.g, c.b, 0f);
            }

            if (glow != null) glow.color = c;
        }

        // Stay as well as Enter so a drop works once room is made while standing on it.
        private void OnTriggerEnter(Collider other) => TryCollect(other);
        private void OnTriggerStay(Collider other) => TryCollect(other);

        private void TryCollect(Collider other)
        {
            if (taken || !other.CompareTag("Player")) return;

            var inventory = Find<PlayerInventory>(other);
            var vault = inventory != null ? inventory.GetComponent<PlayerVault>() : null;
            if (vault == null) vault = Find<PlayerVault>(other);

            string where = Give(inventory, vault);
            if (where == null) return;

            taken = true;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            var playerAudio = Find<PlayerAudio>(other);
            if (playerAudio != null && pickupClip != null) playerAudio.Play2D(pickupClip);

            Debug.Log($"[Loot] {Label} ({Rarity}) -> {where}");
            Destroy(gameObject);
        }

        // Where the item went, or null if it couldn't be taken.
        private string Give(PlayerInventory inventory, PlayerVault vault)
        {
            if (weapon != null)
            {
                if (inventory != null && inventory.TryAddWeapon(weapon)) return "carried";
                if (vault != null && vault.TryAddWeapon(weapon)) return "vault";
            }
            else if (armor != null)
            {
                if (inventory != null && inventory.TryAddArmor(armor)) return "carried";
                if (vault != null && vault.TryAddArmor(armor)) return "vault";
            }
            return null;
        }

        private static string Name(string display, string asset) => string.IsNullOrWhiteSpace(display) ? asset : display;

        private static T Find<T>(Collider other) where T : Component
        {
            var c = other.GetComponentInParent<T>();
            return c != null ? c : other.GetComponentInChildren<T>();
        }
    }
}
