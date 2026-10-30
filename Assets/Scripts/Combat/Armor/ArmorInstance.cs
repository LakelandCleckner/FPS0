using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Perks;

namespace Combat.Armor
{
    // One owned armour piece: definition plus its rolled perks. Also the owner its modifiers are registered under.
    [Serializable]
    public class ArmorInstance
    {
        [SerializeField] private string uid;
        [SerializeField] private ArmorSO definition;
        [SerializeField] private List<PerkSO> perks = new List<PerkSO>();

        public string Uid => uid;
        public ArmorSO Definition => definition;
        public ArmorSlotSO Slot => definition != null ? definition.slot : null;

        // Rolled picks, one per column (null where a column had nothing to roll).
        public IReadOnlyList<PerkSO> Perks => perks;

        public ArmorInstance(ArmorSO definition, RollMode mode = RollMode.Random)
        {
            this.definition = definition;
            uid = Guid.NewGuid().ToString("N");
            Reroll(mode);
        }

        public override string ToString() => definition != null ? "armour " + definition.name : "armour";

        public void Reroll(RollMode mode = RollMode.Random)
        {
            if (definition == null) { perks.Clear(); return; }
            PerkRoll.Roll(definition.ResolvedPerkColumns, definition.curatedRoll, mode, perks);
        }

        // Intrinsic perks, then rolled ones.
        public IEnumerable<PerkSO> AllPerks()
        {
            if (definition != null)
                foreach (var p in definition.intrinsicPerks) if (p != null) yield return p;
            foreach (var p in perks) if (p != null) yield return p;
        }
    }
}