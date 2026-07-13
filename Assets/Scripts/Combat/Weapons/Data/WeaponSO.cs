using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;

namespace Combat.Weapons
{
    // How a specific weapon overrides its archetype's infinite-reserves default.
    public enum InfiniteReservesOverride { UseArchetype, ForceOn, ForceOff }

    // A NAMED WEAPON definition: archetype ref + intrinsic stat deltas + per-weapon
    // damage type + audio + riders + ammo overrides. Base damage is generated, not
    // listed. Perk pool is a placeholder.
    [CreateAssetMenu(fileName = "Weapon", menuName = "Combat/Weapons/Weapon")]
    public class WeaponSO : ScriptableObject
    {
        [Tooltip("The frame/stat profile this weapon uses.")]
        public WeaponArchetypeSO archetype;

        public string id = "";
        public string displayName = "";

        [Header("Element (per-weapon damage type)")]
        public DamageTypeSO baseDamageType;

        [Header("Intrinsic Stat Deltas (deviation from the archetype; 0 = as-is)")]
        public float weaponDamageDelta = 0f;
        public float roundsPerMinuteDelta = 0f;
        public float magazineSizeDelta = 0f;
        public float reloadTimeDelta = 0f;

        [Header("Ammo")]
        [Tooltip("Starting reserve ammo (outside the magazine).")]
        public int startingReserves = 100;
        [Tooltip("Override the archetype's infinite-reserves setting.")]
        public InfiniteReservesOverride infiniteReservesOverride = InfiniteReservesOverride.UseArchetype;

        [Header("Audio (per-weapon identity)")]
        public AudioClip fireClip;
        public AudioClip emptyClip;
        public AudioClip reloadClip;

        [Header("Rider Effects (base damage is generated, NOT listed here)")]
        public List<HitEffectSO> riderEffects = new List<HitEffectSO>();

        [Header("Perk Pool (placeholder — perk system is a later phase)")]
        public List<ScriptableObject> perkPool = new List<ScriptableObject>();

        [Header("Identity / Chain")]
        public int faction = 0;
        public int maxChainDepth = 0;
        public float chainFalloff = 1f;
        public float chainGrowth = 1f;
        public HitDedupMode dedupMode = HitDedupMode.PerShot;

        

        // Resolved infinite-reserves setting (archetype default, weapon override).
        public bool ResolveInfiniteReserves()
        {
            switch (infiniteReservesOverride)
            {
                case InfiniteReservesOverride.ForceOn: return true;
                case InfiniteReservesOverride.ForceOff: return false;
                default: return archetype.infiniteReserves;
            }
        }
    }
}