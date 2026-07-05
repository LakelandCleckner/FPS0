using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;

namespace Combat.Weapons
{
    // A NAMED WEAPON definition: references an archetype for its stat profile,
    // applies its OWN intrinsic stat DELTAS (this weapon's identity), sets its OWN
    // damage type (element — per-weapon), audio, and rider effects (base damage is
    // NOT here — it's generated from the resolved stats).
    //
    // The PERK POOL (what CAN roll) is authored here — placeholder for now.
    [CreateAssetMenu(fileName = "Weapon", menuName = "Combat/Weapons/Weapon")]
    public class WeaponSO : ScriptableObject
    {
        [Tooltip("The frame/stat profile this weapon uses.")]
        public WeaponArchetypeSO archetype;

        [Tooltip("Stable id / display.")]
        public string id = "";
        public string displayName = "";

        [Header("Element (per-weapon damage type)")]
        [Tooltip("This weapon's base damage type. Overridable by upgrades later.")]
        public DamageTypeSO baseDamageType;

        [Header("Intrinsic Stat Deltas (deviation from the archetype; 0 = as-is)")]
        public float weaponDamageDelta = 0f;
        public float critDamageDelta = 0f;
        public float critChanceDelta = 0f;
        public float globalDamageMultiplierDelta = 0f;
        public float roundsPerMinuteDelta = 0f;

        [Header("Audio (per-weapon identity)")]
        public AudioClip fireClip;
        public AudioClip emptyClip;

        [Header("Rider Effects (base damage is generated, NOT listed here)")]
        [Tooltip("On-hit riders only — burn, etc.")]
        public List<HitEffectSO> riderEffects = new List<HitEffectSO>();

        [Header("Perk Pool (placeholder — perk system is a later phase)")]
        [Tooltip("What CAN roll on this weapon. Unused for now.")]
        public List<ScriptableObject> perkPool = new List<ScriptableObject>();

        [Header("Identity / Chain")]
        public int faction = 0;
        public int maxChainDepth = 0;
        public float chainFalloff = 1f;   // neutral
        public float chainGrowth = 1f;    // neutral
        public HitDedupMode dedupMode = HitDedupMode.PerShot;

        // Resolved stats: archetype base + this weapon's intrinsic deltas.
        public StatBlock ResolveStats()
        {
            return new StatBlock(
                weaponDamage: archetype.weaponDamage + weaponDamageDelta,
                critDamage: archetype.critDamage + critDamageDelta,
                critChance: archetype.critChance + critChanceDelta,
                globalMult: archetype.globalDamageMultiplier + globalDamageMultiplierDelta,
                roundsPerMinute: archetype.roundsPerMinute + roundsPerMinuteDelta);
        }
    }
}