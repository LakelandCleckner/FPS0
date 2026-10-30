using System.Collections.Generic;
using UnityEngine;
using Combat.Loadout;

namespace Combat.Perks
{
    // Static modifiers while the item is equipped. The stat-column perk.
    [CreateAssetMenu(fileName = "StatPerk", menuName = "Combat/Perks/Stat Perk")]
    public class StatPerkSO : PerkSO
    {
        [Tooltip("Weapon perk: Weapons = this weapon; Player = gated by this weapon (InLoadout slotted, InHands held).\n" +
                 "Armour perk: Weapons = loadout weapons matching the type filter; Player = always.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        public override PerkRuntime CreateRuntime() => new StatPerkRuntime();

        private sealed class StatPerkRuntime : PerkRuntime
        {
            protected override void OnAttach()
            {
                var def = (StatPerkSO)Definition;
                foreach (var e in def.modifiers)
                    if (e != null) Grant(e, e.value);
            }
        }
    }
}
