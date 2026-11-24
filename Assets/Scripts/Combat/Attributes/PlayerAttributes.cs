using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Loadout;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Attributes
{
    // Allocated points become each attribute's base; every feed is a derived modifier that follows the attribute live.
    public class PlayerAttributes : MonoBehaviour
    {
        [Serializable]
        public class Allocation
        {
            public AttributeSO attribute;
            [Min(0)] public int points;
        }

        [SerializeField] private AttributeLibrarySO library;

        [Tooltip("Found on this object or its parents if empty.")]
        [SerializeField] private CombatantStats player;

        [Tooltip("Found on this object or its children if empty.")]
        [SerializeField] private WeaponLoadout weapons;

        [Tooltip("Points per attribute. Stand-in until player levels hand out points.")]
        [SerializeField] private List<Allocation> allocations = new List<Allocation>();

        private readonly HashSet<WeaponDamageSource> bound = new HashSet<WeaponDamageSource>();
        private bool started;

        public AttributeLibrarySO Library => library;
        public CombatantStats Player => player;

        // Fired when allocated points change.
        public event Action Changed;

        private StatContainer PlayerStats => player != null ? player.Container : null;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<CombatantStats>();
            if (weapons == null) weapons = GetComponentInChildren<WeaponLoadout>();
            if (weapons == null && player != null) weapons = player.GetComponentInChildren<WeaponLoadout>();

            if (library == null) Debug.LogWarning("[Attributes] No attribute library assigned.");
            if (player == null) Debug.LogWarning("[Attributes] No CombatantStats found; attributes are skipped.");
        }

        private void OnEnable()
        {
            if (weapons != null)
            {
                weapons.WeaponBound += HandleBound;
                weapons.WeaponUnbound += HandleUnbound;
            }
            if (player != null) player.ModifiersCleared += ApplyAll;
            if (started) ApplyAll();
        }

        private void Start()
        {
            started = true;
            ApplyAll();
        }

        private void OnDisable()
        {
            if (weapons != null)
            {
                weapons.WeaponBound -= HandleBound;
                weapons.WeaponUnbound -= HandleUnbound;
            }
            if (player != null) player.ModifiersCleared -= ApplyAll;
            StripAll();
        }

#if UNITY_EDITOR
        // Inspector edits apply live in Play mode.
        private void OnValidate()
        {
            if (!Application.isPlaying || !started) return;
            PushBases();
            Changed?.Invoke();
        }
#endif

        // ------------------------------------------------------------------ points

        public int Points(AttributeSO attribute)
        {
            var a = Find(attribute);
            return a != null ? a.points : 0;
        }

        public void SetPoints(AttributeSO attribute, int points)
        {
            if (attribute == null) return;
            var a = Find(attribute);
            if (a == null)
            {
                a = new Allocation { attribute = attribute };
                allocations.Add(a);
            }
            a.points = Mathf.Max(0, points);
            PushBase(attribute);
            Changed?.Invoke();
        }

        public void AddPoints(AttributeSO attribute, int delta) => SetPoints(attribute, Points(attribute) + delta);

        public void ResetPoints()
        {
            foreach (var a in allocations) a.points = 0;
            PushBases();
            Changed?.Invoke();
        }

        private Allocation Find(AttributeSO attribute)
        {
            foreach (var a in allocations)
                if (a != null && a.attribute == attribute) return a;
            return null;
        }

        // ------------------------------------------------------------------ apply

        private void ApplyAll()
        {
            var ps = PlayerStats;
            if (ps == null || library == null) return;

            PushBases();
            foreach (var attr in library.attributes)
            {
                if (!Usable(attr)) continue;
                ps.RemoveAllFromOwner(attr);
                foreach (var f in attr.feeds)
                    if (f != null && f.stat != null && f.target == ModifierTarget.Player)
                        ps.AddModifier(Derive(f, attr, null), attr);
            }

            if (weapons == null) return;
            for (int slot = 0; slot < LoadoutSlots.Count; slot++)
            {
                var c = weapons.ControllerIn(slot);
                if (c != null && c.DamageSource != null) Bind(c.DamageSource);
            }
        }

        private void PushBases()
        {
            if (library == null) return;
            foreach (var attr in library.attributes) PushBase(attr);
        }

        private void PushBase(AttributeSO attr)
        {
            var ps = PlayerStats;
            if (ps == null || !Usable(attr)) return;
            ps.SetBase(attr.stat, Points(attr));
        }

        private void HandleBound(int slot, WeaponDamageSource weapon) => Bind(weapon);

        private void HandleUnbound(int slot, WeaponDamageSource weapon)
        {
            if (weapon == null) return;
            Unbind(weapon);
            bound.Remove(weapon);
        }

        private void Bind(WeaponDamageSource weapon)
        {
            var ps = PlayerStats;
            var wc = weapon != null ? weapon.SourceStats : null;
            if (ps == null || wc == null || library == null) return;

            bound.Add(weapon);
            foreach (var attr in library.attributes)
            {
                if (!Usable(attr)) continue;
                wc.RemoveAllFromOwner(attr);
                foreach (var f in attr.feeds)
                    if (f != null && f.stat != null && f.target == ModifierTarget.Weapons)
                        wc.AddModifier(Derive(f, attr, ps), attr);
            }
        }

        private void Unbind(WeaponDamageSource weapon)
        {
            var wc = weapon != null ? weapon.SourceStats : null;
            if (wc == null || library == null) return;
            foreach (var attr in library.attributes)
                if (attr != null) wc.RemoveAllFromOwner(attr);
        }

        private void StripAll()
        {
            foreach (var w in bound) Unbind(w);
            bound.Clear();

            var ps = PlayerStats;
            if (ps == null || library == null) return;
            foreach (var attr in library.attributes)
                if (attr != null) ps.RemoveAllFromOwner(attr);
        }

        private static StatModifier Derive(AttributeFeed f, AttributeSO attr, StatContainer source)
            => new StatModifier(f.stat, ModifierGrant.IdOf(f.bucket), attr.stat, f.perPoint,
                                scope: ActivityScope.InLoadout, sourceContainer: source);

        private static bool Usable(AttributeSO attr)
        {
            if (attr == null || attr.stat == null) return false;
            if (StatRegistry.IndexOf(attr.stat) >= 0) return true;
            Debug.LogWarning($"[Attributes] '{attr.name}' stat isn't in StatBootstrap; skipped.");
            return false;
        }
    }
}
