using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Delivery;

namespace Combat.Sources
{
    // Weapon damage source. Now also implements IProjectileSource so it can feed
    // projectile params to ProjectileStrategy. A hitscan-only weapon could skip
    // the interface, but implementing it harmlessly lets one weapon support both
    // delivery types and swap between them via upgrade.
    public class WeaponDamageSource : MonoBehaviour, IDamageSource, IProjectileSource
    {
        [Header("Stats")]
        [SerializeField] private float weaponDamage = 20f;
        [SerializeField] private float critDamage = 40f;
        [SerializeField] private float critChance = 0.1f;
        [SerializeField] private float globalDamageMultiplier = 1f;

        [Header("Effects (authored + runtime-mutable)")]
        [SerializeField] private List<HitEffectSO> effectDefinitions = new List<HitEffectSO>();

        [Header("Identity")]
        [SerializeField] private int faction = 0;
        [SerializeField] private DamageTypeSO baseDamageType;

        [Header("Chain Config")]
        [SerializeField] private int maxChainDepth = 0;
        [SerializeField] private float chainFalloff = 1f;
        [SerializeField] private float chainGrowth = 1f;
        [SerializeField] private HitDedupMode dedupMode = HitDedupMode.PerShot;

        [Header("Projectile Config (used only by projectile delivery)")]
        [SerializeField] private ProjectileConfig projectileConfig = new ProjectileConfig();

        private List<IHitEffect> cachedEffects;

        private void Awake() => RebuildEffects();

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
        public List<IHitEffect> GetEffects() => cachedEffects;
        public int Faction => faction;
        public DamageTypeSO BaseDamageType => baseDamageType;
        public int MaxChainDepth => maxChainDepth;
        public float ChainFalloff => chainFalloff;
        public float ChainGrowth => chainGrowth;
        public HitDedupMode DedupMode => dedupMode;

        // ---- IProjectileSource ----
        public ProjectileConfig GetProjectileConfig() => projectileConfig;

        // ---- Upgrade hooks ----
        public void AddEffect(HitEffectSO def) { effectDefinitions.Add(def); RebuildEffects(); }
        public void RemoveEffect(HitEffectSO def) { effectDefinitions.Remove(def); RebuildEffects(); }
        public void SetEffects(List<HitEffectSO> defs) { effectDefinitions = new List<HitEffectSO>(defs); RebuildEffects(); }

        // projectile upgrade hooks delegate to the config
        public void AddPierce(int amount) => projectileConfig.AddPierce(amount);
    }
}