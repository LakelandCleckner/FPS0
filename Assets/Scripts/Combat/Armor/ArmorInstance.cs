using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Perks;

namespace Combat.Armor
{
    // One owned armor piece: definition plus its rolled perks. Also the owner its modifiers are registered under.
    [Serializable]
    public class ArmorInstance
    {
        [SerializeField] private string uid;
        [SerializeField] private ArmorSO definition;
        [SerializeField] private List<PerkSO> perks = new List<PerkSO>();
        [SerializeField] private Rarity rarity;

        public string Uid => uid;
        public ArmorSO Definition => definition;
        public ArmorSlotSO Slot => definition != null ? definition.slot : null;

        // Rolled picks, one per column (null where a column had nothing to roll).
        public IReadOnlyList<PerkSO> Perks => perks;
        public Rarity Rarity => rarity;

        // Mythic definitions are always Mythic.
        public ArmorInstance(ArmorSO definition, RollMode mode = RollMode.Random, Rarity rarity = Rarity.Common)
        {
            this.definition = definition;
            this.rarity = definition != null && definition.mythic ? Rarity.Mythic : rarity;
            uid = Guid.NewGuid().ToString("N");
            Reroll(mode);
        }

        public override string ToString() => definition != null ? "armor " + definition.name : "armor";

        // New perks at the same rarity. Mythic items always use their curated picks.
        public void Reroll(RollMode mode = RollMode.Random)
        {
            if (definition == null) { perks.Clear(); return; }
            if (definition.mythic) mode = RollMode.Curated;
            PerkRoll.Roll(definition.ResolvedPerkColumns, definition.curatedRoll, mode, perks, definition.intrinsicPerks, definition.name, rarity);
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