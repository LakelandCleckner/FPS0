using System.Collections.Generic;
using Combat.Armor;
using Combat.Loadout;
using Combat.Stats;

namespace Combat.Weapons
{
    // One stat seen three ways: the weapon alone, plus its own perks, plus equipped armor.
    public struct StatLayers
    {
        public float Base;
        public float WithPerks;
        public float WithArmor;

        public float PerkDelta => WithPerks - Base;
        public float ArmorDelta => WithArmor - WithPerks;
    }

    // Stat preview for any owned weapon, equipped or not. Only unconditional perk modifiers count.
    public sealed class WeaponStatPreview
    {
        private StatContainer baseStats, perkStats, fullStats;
        private readonly List<ModifierEntry> scratch = new List<ModifierEntry>();

        public bool IsBuilt { get; private set; }

        public bool Build(WeaponInstance weapon, IEnumerable<ArmorInstance> armor, WeaponStatKeys keys)
        {
            IsBuilt = false;
            if (weapon == null || weapon.Definition == null || keys == null || StatRegistry.Count <= 0) return false;

            var def = weapon.Definition;
            baseStats = new StatContainer();
            perkStats = new StatContainer();
            fullStats = new StatContainer();
            WeaponStatBuilder.PopulateBases(baseStats, def, keys);
            WeaponStatBuilder.PopulateBases(perkStats, def, keys);
            WeaponStatBuilder.PopulateBases(fullStats, def, keys);

            scratch.Clear();
            foreach (var perk in weapon.AllPerks()) perk.CollectStaticModifiers(scratch);
            foreach (var e in scratch)
            {
                if (e.target != ModifierTarget.Weapons) continue;
                Add(perkStats, e);
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
                    foreach (var e in scratch)
                        if (e.target == ModifierTarget.Weapons && TypeMatches(e, type)) Add(fullStats, e);
                }
            }

            IsBuilt = true;
            return true;
        }

        public StatLayers Get(StatDefinitionSO stat)
        {
            if (!IsBuilt || stat == null) return default;
            return new StatLayers
            {
                Base = baseStats.Resolve(stat),
                WithPerks = perkStats.Resolve(stat),
                WithArmor = fullStats.Resolve(stat)
            };
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
