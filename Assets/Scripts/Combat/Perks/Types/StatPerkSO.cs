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
                 "Armor perk: Weapons = loadout weapons matching the type filter; Player = always.")]
        public List<ModifierEntry> modifiers = new List<ModifierEntry>();

        public override PerkRuntime CreateRuntime() => new StatPerkRuntime();

        public override void CollectStaticModifiers(List<ModifierEntry> into)
        {
            foreach (var e in modifiers)
                if (e != null && e.stat != null) into.Add(e);
        }

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
