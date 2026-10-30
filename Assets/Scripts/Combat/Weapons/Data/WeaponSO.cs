using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;

namespace Combat.Weapons
{
    // How a specific weapon overrides its archetype's infinite-reserves default.
    public enum InfiniteReservesOverride { UseArchetype, ForceOn, ForceOff }

    // One material replacement on the spawned gun model.
    [System.Serializable]
    public struct MaterialSwap
    {
        public Material from;
        public Material to;
    }

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

        [Header("Presentation")]
        public WeaponViewmodelSO viewmodel;
        [Tooltip("Material swaps applied to the gun model on spawn. Empty = prefab defaults.")]
        public List<MaterialSwap> viewmodelSkin = new List<MaterialSwap>();
        [Tooltip("Zone shader preset applied to the gun model on spawn. Empty = original look.")]
        public WeaponShaderSO defaultShader;

        [Header("Loadout")]
        [Tooltip("Use the slot rule below instead of the weapon type's.")]
        public bool overrideSlots = false;
        public LoadoutSlotMask allowedSlotsOverride = LoadoutSlotMask.All;

        [Header("Element (per-weapon damage type)")]
        public DamageTypeSO baseDamageType;

        [Header("Intrinsic Stat Deltas (deviation from the archetype; 0 = as-is)")]
        public float weaponDamageDelta = 0f;
        public float roundsPerMinuteDelta = 0f;
        public float magazineSizeDelta = 0f;
        public float reloadTimeDelta = 0f;
        public float handlingDelta = 0f;
        [Tooltip("ADS magnification delta. ADS time has no delta; it follows handling.")]
        public float adsZoomDelta = 0f;
        [Tooltip("Range stat delta (stat caps at 100).")]
        public float rangeDelta = 0f;
        [Tooltip("Aim assist stat delta (stat caps at 100).")]
        public float aimAssistDelta = 0f;

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

        [Header("Perk Pool (placeholder -- perk system is a later phase)")]
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

        // Slots this weapon may occupy: the override, else its type's rule.
        public LoadoutSlotMask AllowedSlots
        {
            get
            {
                if (overrideSlots) return allowedSlotsOverride;
                var type = archetype != null ? archetype.weaponType : null;
                return type != null ? type.allowedSlots : LoadoutSlotMask.All;
            }
        }
    }
}