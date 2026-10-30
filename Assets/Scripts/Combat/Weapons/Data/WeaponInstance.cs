using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Perks;

namespace Combat.Weapons
{
    // One owned weapon: the definition plus everything that differs per copy.
    [Serializable]
    public class WeaponInstance
    {
        [SerializeField] private string uid;
        [SerializeField] private WeaponSO definition;
        [SerializeField] private List<PerkSO> perks = new List<PerkSO>();

        [Tooltip("Rounds in the magazine. -1 = full on first spawn.")]
        public int magazine = -1;

        [Tooltip("Own reserves, used when the archetype has no ammo pool. -1 = starting reserves.")]
        public int reserves = -1;

        public string Uid => uid;
        public WeaponSO Definition => definition;

        // Rolled picks, one per column (null where a column had nothing to roll).
        public IReadOnlyList<PerkSO> Perks => perks;

        public WeaponInstance(WeaponSO definition, RollMode mode = RollMode.Random)
        {
            this.definition = definition;
            uid = Guid.NewGuid().ToString("N");
            Reroll(mode);
        }

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