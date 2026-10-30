using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Weapons;

namespace Combat.Loadout
{
    // What the player owns and has equipped at the start.
    [CreateAssetMenu(fileName = "StartingLoadout", menuName = "Combat/Loadout/Starting Loadout")]
    public class StartingLoadoutSO : ScriptableObject
    {
        [Serializable]
        public struct PoolAmount
        {
            public AmmoPoolSO pool;
            public int amount;
        }

        [Tooltip("Weapons in slots 1-3. Empty entry = empty slot.")]
        public WeaponSO[] equipped = new WeaponSO[LoadoutSlots.Count];

        [Tooltip("Weapons carried but not equipped.")]
        public List<WeaponSO> carried = new List<WeaponSO>();

        [Tooltip("Starting reserves for shared ammo pools.")]
        public List<PoolAmount> ammoPools = new List<PoolAmount>();
    }
}
