using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Loadout;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Attributes
{
    // Attribute debug commands. Public so a debug panel can call them; keys come from the Debug input map.
    public class AttributeDebugKeys : MonoBehaviour
    {
        [Tooltip("Found in parents or children if empty.")]
        [SerializeField] private PlayerAttributes attributes;

        [Tooltip("Points added or removed per press.")]
        [SerializeField, Min(1)] private int step = 10;

        private PlayerInputRouter input;
        private InputAction next, add, remove, reset, dump;
        private int selected;

        public AttributeSO Selected
        {
            get
            {
                var list = attributes != null && attributes.Library != null ? attributes.Library.attributes : null;
                if (list == null || list.Count == 0) return null;
                selected = (selected % list.Count + list.Count) % list.Count;
                return list[selected];
            }
        }

        private void Awake()
        {
            if (attributes == null) attributes = GetComponentInParent<PlayerAttributes>();
            if (attributes == null) attributes = GetComponentInChildren<PlayerAttributes>();
            input = PlayerInputRouter.For(this);
        }

        private void Update()
        {
            if (input == null) input = PlayerInputRouter.For(this);
            if (input == null || attributes == null) return;
            if (next == null && !FindActions()) return;

            if (next.WasPressedThisFrame()) SelectNext();
            if (add.WasPressedThisFrame()) Add(step);
            if (remove.WasPressedThisFrame()) Add(-step);
            if (reset.WasPressedThisFrame()) ResetAll();
            if (dump.WasPressedThisFrame()) Dump();
        }

        // Looked up by name so this compiles before the input wrapper regenerates.
        private bool FindActions()
        {
            var asset = input.Inputs.asset;
            next = asset.FindAction("Debug/AttributeNext");
            add = asset.FindAction("Debug/AttributeAdd");
            remove = asset.FindAction("Debug/AttributeRemove");
            reset = asset.FindAction("Debug/AttributeReset");
            dump = asset.FindAction("Debug/AttributeDump");
            if (next != null && add != null && remove != null && reset != null && dump != null) return true;

            next = null;
            Debug.LogWarning("[AttributeDebug] Attribute actions missing from the Debug input map.");
            enabled = false;
            return false;
        }

        public void SelectNext()
        {
            selected++;
            var a = Selected;
            if (a != null) Debug.Log($"[AttributeDebug] Selected {a.DisplayName} ({attributes.Points(a)})");
        }

        public void Add(int delta)
        {
            var a = Selected;
            if (a == null) return;
            attributes.AddPoints(a, delta);
            Debug.Log($"[AttributeDebug] {a.DisplayName} = {attributes.Points(a)}");
        }

        public void ResetAll()
        {
            attributes.ResetPoints();
            Debug.Log("[AttributeDebug] All attribute points reset.");
        }

        // Each attribute's points and resolved value, and every stat it feeds (held weapon for weapon feeds).
        public void Dump()
        {
            var ps = attributes.Player != null ? attributes.Player.Container : null;
            var lib = attributes.Library;
            if (ps == null || lib == null) return;

            var held = GetComponentInParent<WeaponLoadout>();
            if (held == null && attributes.Player != null) held = attributes.Player.GetComponentInChildren<WeaponLoadout>();
            var wc = held != null && held.Active != null && held.Active.DamageSource != null ? held.Active.DamageSource.SourceStats : null;

            var sb = new StringBuilder("[AttributeDebug] Attributes\n");
            foreach (var a in lib.attributes)
            {
                if (a == null || a.stat == null) continue;
                sb.Append(a.DisplayName).Append(": ").Append(attributes.Points(a)).Append(" allocated, ")
                  .Append(ps.Resolve(a.stat).ToString("0.##")).Append(" total\n");
                foreach (var f in a.feeds)
                {
                    if (f == null || f.stat == null) continue;
                    var c = f.target == ModifierTarget.Player ? ps : wc;
                    sb.Append("    ").Append(f.stat.id).Append(" = ")
                      .Append(c != null ? c.Resolve(f.stat).ToString("0.###") : "(no held weapon)").Append('\n');
                }
            }
            Debug.Log(sb.ToString());
        }
    }
}
