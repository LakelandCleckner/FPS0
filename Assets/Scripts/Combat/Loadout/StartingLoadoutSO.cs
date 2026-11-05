using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Loot;
using Combat.Perks;
using Combat.Weapons;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

        [Tooltip("How every item created from this loadout rolls its perks.")]
        public RollMode rollMode = RollMode.Random;

        [Header("Weapons")]
        [Tooltip("Weapons in slots 1-3. Empty entry = empty slot.")]
        public WeaponSO[] equipped = new WeaponSO[LoadoutSlots.Count];

        [Tooltip("Weapons carried but not equipped.")]
        public List<WeaponSO> carried = new List<WeaponSO>();

        [Tooltip("Starting reserves for shared ammo pools.")]
        public List<PoolAmount> ammoPools = new List<PoolAmount>();

        [Header("Armor")]
        [Tooltip("Armor equipped at start; each goes to its own slot. Extras for a filled slot are carried.")]
        public List<ArmorSO> equippedArmor = new List<ArmorSO>();

        [Tooltip("Armor carried but not equipped.")]
        public List<ArmorSO> carriedArmor = new List<ArmorSO>();

        [Header("Rarity")]
        [Tooltip("Odds for each item's rarity. Mythic only comes from Mythic definitions.")]
        [Min(0)] public int commonWeight = 60;
        [Min(0)] public int uncommonWeight = 30;
        [Min(0)] public int rareWeight = 30;
        [Min(0)] public int legendaryWeight = 10;

        public Rarity RollRarity() => RarityWeights.Roll(commonWeight, uncommonWeight, rareWeight, legendaryWeight);

        [Header("Random (testing)")]
        [Tooltip("Ignore the lists above and roll a random loadout every play.")]
        public bool randomize = false;
        [Tooltip("Weapons to pick from. Empty in the editor = every weapon in the project.")]
        public List<WeaponSO> weaponPool = new List<WeaponSO>();
        [Tooltip("Armor to pick from. Empty in the editor = every armor piece in the project.")]
        public List<ArmorSO> armorPool = new List<ArmorSO>();
        [Min(0)] public int randomCarriedWeapons = 6;
        [Min(0)] public int randomCarriedArmor = 5;

        // The authored pool, or every asset of the type in the editor when it's empty.
        public List<WeaponSO> WeaponPool() => Resolve(weaponPool);
        public List<ArmorSO> ArmorPool() => Resolve(armorPool);

        private static List<T> Resolve<T>(List<T> pool) where T : ScriptableObject
        {
            var list = new List<T>();
            foreach (var x in pool) if (x != null) list.Add(x);
#if UNITY_EDITOR
            if (list.Count == 0) list = FindAll<T>();
#endif
            return list;
        }

#if UNITY_EDITOR
        private static List<T> FindAll<T>() where T : ScriptableObject
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) list.Add(a);
            }
            return list;
        }

        // Bakes every weapon and armor asset into the pools, for builds that can't scan the project.
        [ContextMenu("Fill Pools From Project")]
        private void FillPools()
        {
            Undo.RecordObject(this, "Fill Pools");
            weaponPool = FindAll<WeaponSO>();
            armorPool = FindAll<ArmorSO>();
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
