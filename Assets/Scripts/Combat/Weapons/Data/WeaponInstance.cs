using System;
using UnityEngine;

namespace Combat.Weapons
{
    // One owned weapon: the definition plus everything that differs per copy.
    [Serializable]
    public class WeaponInstance
    {
        [SerializeField] private string uid;
        [SerializeField] private WeaponSO definition;

        [Tooltip("Rounds in the magazine. -1 = full on first spawn.")]
        public int magazine = -1;

        [Tooltip("Own reserves, used when the archetype has no ammo pool. -1 = starting reserves.")]
        public int reserves = -1;

        public string Uid => uid;
        public WeaponSO Definition => definition;

        public WeaponInstance(WeaponSO definition)
        {
            this.definition = definition;
            uid = Guid.NewGuid().ToString("N");
        }
    }
}
