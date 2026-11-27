using System.Collections.Generic;
using Combat.Affixes;
using Combat.Armor;
using Combat.Attributes;
using Combat.Loadout;
using Combat.Stats;

namespace Combat.Weapons
{
    // One stat seen five ways: the weapon alone, plus its affixes, plus its own perks, plus equipped armor, plus attributes.
    public struct StatLayers
    {
        public float Base;
        public float WithAffixes;
        public float WithPerks;
        public float WithArmor;
        public float WithAttributes;

        public float Final => WithAttributes;
        public float AffixDelta => WithAffixes - Base;
        public float PerkDelta => WithPerks - WithAffixes;
        public float ArmorDelta => WithArmor - WithPerks;
        public float AttributeDelta => WithAttributes - WithArmor;
    }

    // Stat preview for any owned weapon, equipped or not. Only unconditional perk modifiers count.
    public sealed class WeaponStatPreview
    {
        private StatContainer baseStats, affixStats, perkStats, armorStats, fullStats;
        private readonly List<ModifierEntry> scratch = new List<ModifierEntry>();

        public bool IsBuilt { get; private set; }

        // Attributes are read live from the player, as they would apply once this weapon is in the loadout.
        public bool Build(WeaponInstance weapon, IEnumerable<ArmorInstance> armor, WeaponStatKeys keys,
                          PlayerAttributes attributes = null)
        {
            IsBuilt = false;
            if (weapon == null || weapon.Definition == null || keys == null || StatRegistry.Count <= 0) return false;

            var def = weapon.Definition;
            baseStats = new StatContainer();
            affixStats = new StatContainer();
            perkStats = new StatContainer();
            armorStats = new StatContainer();
            fullStats = new StatContainer();
            int level = weapon.ItemLevel;
            WeaponStatBuilder.PopulateBases(baseStats, def, keys, level);
            WeaponStatBuilder.PopulateBases(affixStats, def, keys, level);
            WeaponStatBuilder.PopulateBases(perkStats, def, keys, level);
            WeaponStatBuilder.PopulateBases(armorStats, def, keys, level);
            WeaponStatBuilder.PopulateBases(fullStats, def, keys, level);

            foreach (var roll in weapon.Affixes)
            {
                if (roll == null || roll.affix == null || roll.affix.target != ModifierTarget.Weapons) continue;
                var e = roll.affix.Entry(roll.Value(weapon.Rarity, level));
                Add(affixStats, e);
                Add(perkStats, e);
                Add(armorStats, e);
                Add(fullStats, e);
            }

            scratch.Clear();
            foreach (var perk in weapon.AllPerks()) perk.CollectStaticModifiers(scratch);
            foreach (var e in scratch)
            {
                if (e.target != ModifierTarget.Weapons) continue;
                Add(perkStats, e);
                Add(armorStats, e);
                Add(fullStats, e);
            }

            if (armor != null)
            {
                var type = def.archetype != null ? def.archetype.weaponType : null;
                foreach (var piece in armor)
                {
                    if (piece == null) continue;
                    scratch.Clear();
                    foreach (var perk in piece.AllPerks()) perk.CollectStaticModifiers(scratch);
                    foreach (var roll in piece.Affixes)
                        if (roll != null && roll.affix != null)
                            scratch.Add(roll.affix.Entry(roll.Value(piece.Rarity, piece.ItemLevel)));
                    foreach (var e in scratch)
                        if (e.target == ModifierTarget.Weapons && TypeMatches(e, type))
                        {
                            Add(armorStats, e);
                            Add(fullStats, e);
                        }
                }
            }

            AddAttributes(fullStats, attributes);

            IsBuilt = true;
            return true;
        }

        public StatLayers Get(StatDefinitionSO stat)
        {
            if (!IsBuilt || stat == null) return default;
            return new StatLayers
            {
                Base = baseStats.Resolve(stat),
                WithAffixes = affixStats.Resolve(stat),
                WithPerks = perkStats.Resolve(stat),
                WithArmor = armorStats.Resolve(stat),
                WithAttributes = fullStats.Resolve(stat)
            };
        }

        private static void AddAttributes(StatContainer c, PlayerAttributes attributes)
        {
            var lib = attributes != null ? attributes.Library : null;
            var ps = attributes != null && attributes.Player != null ? attributes.Player.Container : null;
            if (lib == null || ps == null) return;

            foreach (var attr in lib.attributes)
            {
                if (attr == null || attr.stat == null || StatRegistry.IndexOf(attr.stat) < 0) continue;
                foreach (var f in attr.feeds)
                    if (f != null && f.stat != null && f.target == ModifierTarget.Weapons)
                        c.AddModifier(new StatModifier(f.stat, ModifierGrant.IdOf(f.bucket), attr.stat, f.perPoint,
                                                       scope: ActivityScope.InLoadout, sourceContainer: ps), attr);
            }
        }

        private static void Add(StatContainer c, ModifierEntry e)
            => c.AddModifier(new StatModifier(e.stat, ModifierGrant.IdOf(e.bucket), e.value, e.scope), e);

        private static bool TypeMatches(ModifierEntry e, WeaponTypeSO type)
        {
            if (e.weaponTypes == null || e.weaponTypes.Count == 0) return true;
            return type != null && e.weaponTypes.Contains(type);
        }
    }
}
