using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Loadout
{
    public enum ModifierBucket { Flat, Additive, Multiplicative }

    public enum ModifierTarget { Player, Weapons }

    // One authored stat change, used by armour and perks.
    [Serializable]
    public class ModifierEntry
    {
        public StatDefinitionSO stat;

        [Tooltip("Flat adds to the base. Additive: percent on ScaledBase stats (0.1 = +10%), " +
                 "a straight add on Pool stats. Multiplicative: its own x(1 + value).")]
        public ModifierBucket bucket = ModifierBucket.Flat;

        public float value;

        [Tooltip("Player stats, or weapon stats.")]
        public ModifierTarget target = ModifierTarget.Player;

        [Tooltip("Armour, Weapons target only. Empty = every weapon type.")]
        public List<WeaponTypeSO> weaponTypes = new List<WeaponTypeSO>();

        [Tooltip("InLoadout = while slotted; InHands = only while held.")]
        public ActivityScope scope = ActivityScope.InLoadout;
    }

    // One modifier a loadout source (armour, later perks) wants applied while its conditions hold.
    public sealed class ModifierGrant
    {
        public readonly object Owner;
        public readonly StatDefinitionSO Stat;
        public readonly string BucketId;
        // Changed through LoadoutModifierApplier.SetValue so containers refresh.
        public float Value { get; internal set; }
        public readonly ModifierTarget Target;
        public readonly ActivityScope Scope;

        // Weapons target: limit to these types (empty = all) and/or one specific weapon.
        public readonly IReadOnlyList<WeaponTypeSO> WeaponTypes;
        public readonly WeaponDamageSource OnlyWeapon;

        // Player target: live only while this weapon is slotted (InLoadout) or held (InHands). Null = always.
        public readonly WeaponDamageSource GateWeapon;

        public ModifierGrant(object owner, StatDefinitionSO stat, ModifierBucket bucket, float value,
                             ModifierTarget target, ActivityScope scope,
                             IReadOnlyList<WeaponTypeSO> weaponTypes = null,
                             WeaponDamageSource onlyWeapon = null,
                             WeaponDamageSource gateWeapon = null)
        {
            Owner = owner;
            Stat = stat;
            BucketId = IdOf(bucket);
            Value = value;
            Target = target;
            Scope = scope;
            WeaponTypes = weaponTypes;
            OnlyWeapon = onlyWeapon;
            GateWeapon = gateWeapon;
        }

        public static string IdOf(ModifierBucket bucket)
        {
            switch (bucket)
            {
                case ModifierBucket.Additive: return StatResolver.ADDITIVE;
                case ModifierBucket.Multiplicative: return StatResolver.MULTIPLICATIVE;
                default: return StatResolver.FLAT;
            }
        }

        // Weapons target: does this weapon pass the type and single-weapon filters?
        public bool Matches(WeaponDamageSource weapon)
        {
            if (weapon == null) return false;
            if (OnlyWeapon != null && OnlyWeapon != weapon) return false;
            if (WeaponTypes == null || WeaponTypes.Count == 0) return true;

            var def = weapon.Weapon;
            var type = def != null && def.archetype != null ? def.archetype.weaponType : null;
            if (type == null) return false;

            for (int i = 0; i < WeaponTypes.Count; i++)
                if (WeaponTypes[i] == type) return true;
            return false;
        }

        public override string ToString() => Owner != null ? Owner.ToString() : "grant";

        // A fresh modifier per container: StatModifier stores its handle.
        public StatModifier CreateModifier() => new StatModifier(Stat, BucketId, Value, Scope);
    }
}