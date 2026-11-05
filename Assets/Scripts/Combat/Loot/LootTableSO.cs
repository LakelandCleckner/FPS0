using System.Collections.Generic;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;
using Combat.Weapons;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Combat.Loot
{
    // What a source can drop, how often, and at which rarity.
    [CreateAssetMenu(fileName = "LootTable", menuName = "Combat/Loot/Loot Table")]
    public class LootTableSO : ScriptableObject
    {
        [Header("Drop")]
        [Tooltip("Chance per death that anything drops.")]
        [Range(0f, 1f)] public float dropChance = 0.3f;
        [Tooltip("Prefab spawned in the world for each drop.")]
        public ItemDrop dropPrefab;
        [Tooltip("What drops land on. Leave enemy and corpse layers out.")]
        public LayerMask groundMask = ~0;

        [Header("Type")]
        [Min(0)] public int weaponWeight = 1;
        [Min(0)] public int armorWeight = 1;

        [Header("Roll")]
        public RollMode rollMode = RollMode.Random;
        [Tooltip("Mythic drops a hand-made Mythic from the pools; other rarities never pick one.")]
        public RarityWeights rarity = RarityWeights.Default;

        [Header("Pools")]
        [Tooltip("Empty in the editor = every weapon in the project.")]
        public List<WeaponSO> weapons = new List<WeaponSO>();
        [Tooltip("Empty in the editor = every armor piece in the project.")]
        public List<ArmorSO> armor = new List<ArmorSO>();

        private const float GroundProbe = 6f;
        private bool warnedNoMythic;

        // Rolls and spawns one drop at the position. Null when nothing drops.
        public ItemDrop SpawnDrop(Vector3 position, bool ignoreChance = false)
        {
            if (dropPrefab == null) { Debug.LogWarning($"[Loot] {name} has no drop prefab."); return null; }
            if (!ignoreChance && Random.value >= dropChance) return null;
            if (!Roll(out var weapon, out var piece)) return null;

            var drop = Instantiate(dropPrefab, Ground(position), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (weapon != null) drop.Init(weapon);
            else drop.Init(piece);
            return drop;
        }

        // One rolled weapon or armor piece; false when both pools are empty.
        public bool Roll(out WeaponInstance weapon, out ArmorInstance piece)
        {
            weapon = null;
            piece = null;

            var allW = Resolve(weapons);
            var allA = Resolve(armor);
            var r = rarity.Roll();

            bool mythic = r == Rarity.Mythic;
            var w = allW.FindAll(d => d.mythic == mythic);
            var a = allA.FindAll(d => d.mythic == mythic);

            // No hand-made Mythic to give: drop a Legendary instead.
            if (mythic && w.Count == 0 && a.Count == 0)
            {
                if (!warnedNoMythic) { warnedNoMythic = true; Debug.LogWarning($"[Loot] {name} rolled Mythic but has no Mythic items; dropping Legendary."); }
                r = Rarity.Legendary;
                w = allW.FindAll(d => !d.mythic);
                a = allA.FindAll(d => !d.mythic);
            }

            int ww = w.Count > 0 ? weaponWeight : 0;
            int aw = a.Count > 0 ? armorWeight : 0;
            if (ww + aw <= 0) return false;

            if (Random.Range(0, ww + aw) < ww)
                weapon = new WeaponInstance(w[Random.Range(0, w.Count)], rollMode, r);
            else
                piece = new ArmorInstance(a[Random.Range(0, a.Count)], rollMode, r);
            return true;
        }

        private Vector3 Ground(Vector3 p)
        {
            var origin = p + Vector3.up * 1f;
            return Physics.Raycast(origin, Vector3.down, out var hit, GroundProbe, groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point : p;
        }

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
            weapons = FindAll<WeaponSO>();
            armor = FindAll<ArmorSO>();
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
