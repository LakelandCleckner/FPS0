using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;

namespace Combat.Sources
{
    // Concrete IDamageSource for a weapon. Owns stats, the inspector-authored
    // effect list, and chain config. The upgrade system mutates the effect list
    // at runtime via AddEffect/RemoveEffect, which rebuilds the cached instance
    // list — so shots themselves allocate nothing.
    public class WeaponDamageSource : MonoBehaviour, IDamageSource
    {
        [Header("Stats")]
        [SerializeField] private float weaponDamage = 20f;
        [SerializeField] private float critDamage = 40f;
        [SerializeField] private float critChance = 0.1f;
        [SerializeField] private float globalDamageMultiplier = 1f;

        [Header("Effects (authored + runtime-mutable)")]
        [SerializeField] private List<HitEffectSO> effectDefinitions = new List<HitEffectSO>();

        [Header("Identity")]
        [SerializeField] private int faction = 0; // 0 = player
        [SerializeField] private DamageType baseDamageType = DamageType.Physical;

        [Header("Chain Config")]
        [SerializeField] private int maxChainDepth = 0;
        [SerializeField] private float chainFalloff = 1f;
        [SerializeField] private float chainGrowth = 1f;
        [SerializeField] private HitDedupMode dedupMode = HitDedupMode.PerShot;

        // Cached instance list — rebuilt only when the effect list changes.
        private List<IHitEffect> cachedEffects;

        private void Awake()
        {
            RebuildEffects();
        }

        private void RebuildEffects()
        {
            cachedEffects = new List<IHitEffect>(effectDefinitions.Count);
            foreach (var def in effectDefinitions)
                if (def != null)
                    cachedEffects.Add(def.GetInstance());
        }

        // ---- IDamageSource ----
        public StatBlock GetStats() =>
            new StatBlock(weaponDamage, critDamage, critChance, globalDamageMultiplier);

        public List<IHitEffect> GetEffects() => cachedEffects; // no per-shot alloc

        public int Faction => faction;
        public DamageType BaseDamageType => baseDamageType;
        public int MaxChainDepth => maxChainDepth;
        public float ChainFalloff => chainFalloff;
        public float ChainGrowth => chainGrowth;
        public HitDedupMode DedupMode => dedupMode;

        // ---- Upgrade hooks ----
        public void AddEffect(HitEffectSO def)
        {
            effectDefinitions.Add(def);
            RebuildEffects();
        }

        public void RemoveEffect(HitEffectSO def)
        {
            effectDefinitions.Remove(def);
            RebuildEffects();
        }

        public void SetEffects(List<HitEffectSO> defs)
        {
            effectDefinitions = new List<HitEffectSO>(defs);
            RebuildEffects();
        }
    }
}
