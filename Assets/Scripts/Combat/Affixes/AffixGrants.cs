using System.Collections.Generic;
using Combat.Loadout;
using Combat.Perks;
using Combat.Sources;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Affixes
{
    // An item's affixes as live modifiers. Weapon affixes boost that weapon; player ones apply while it's slotted.
    // Armor affixes boost the player, or loadout weapons of their target types.
    public static class AffixGrants
    {
        // Labels a registered affix in stat breakdowns: "Range (Lightning Rod)".
        private sealed class Source : IStatSourceLabel
        {
            private readonly string label;
            public Source(string label) { this.label = label; }
            public string StatSourceLabel => label;
        }

        // Registers each affix and adds its owner to owners, for Unregister.
        public static void Register(LoadoutModifierApplier applier, IReadOnlyList<AffixRoll> rolls, Rarity rarity, int itemLevel,
                                    string itemName, WeaponDamageSource weapon, List<object> owners)
        {
            if (applier == null || rolls == null) return;
            foreach (var roll in rolls)
            {
                var a = roll != null ? roll.affix : null;
                if (a == null || a.stat == null) continue;

                WeaponDamageSource only = null, gate = null;
                IReadOnlyList<WeaponTypeSO> types = null;
                if (a.target == ModifierTarget.Weapons)
                {
                    if (weapon == null) types = a.targetWeaponTypes;
                    else only = weapon;
                }
                else if (weapon != null) gate = weapon;

                var owner = new Source(string.IsNullOrEmpty(itemName) ? a.DisplayName : a.DisplayName + " (" + itemName + ")");
                applier.Register(new ModifierGrant(owner, a.stat, a.bucket, roll.Value(rarity, itemLevel), a.target, a.scope, types, only, gate));
                owners.Add(owner);
            }
        }

        public static void Unregister(LoadoutModifierApplier applier, List<object> owners)
        {
            if (applier != null)
                foreach (var o in owners) applier.Unregister(o);
            owners.Clear();
        }
    }
}
