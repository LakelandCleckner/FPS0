using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Delivery;
using Combat.Weapons;

namespace Combat.Sources
{
    // Runtime weapon: the live gun in-hand. References a WeaponSO (its authored
    // definition) and resolves everything from the SO hierarchy:
    //   archetype base stats + weapon intrinsic deltas -> StatBlock (cached)
    //   generated base-damage effect (PercentOfWeapon 1.0, weapon's type) + riders
    //
    // Base damage is NO LONGER a hand-placed effect — it's synthesized from the
    // resolved stats + the weapon's type and cached (no per-shot allocation).
    // Stats/effects are cached and rebuilt on change (invalidation hook for when
    // perks/upgrades that change stats arrive).
    //
    // Delivery still lives on the weapon object for now (fire-mode system is 1b).
    // Still implements IProjectileSource; projectile config comes from the WeaponSO
    // definition's... (Phase 1a: projectile config kept on the component until the
    // fire-mode phase moves delivery into archetype data.)
    public class WeaponDamageSource : MonoBehaviour, IDamageSource, IProjectileSource
    {
        [Header("Weapon Definition")]
        [SerializeField] private WeaponSO weapon;

        [Header("Projectile Config (delivery still on the object until fire-mode phase)")]
        [SerializeField] private ProjectileConfig projectileConfig = new ProjectileConfig();

        // cached resolved snapshot + effect list (base damage + riders)
        private StatBlock cachedStats;
        private List<IHitEffect> cachedEffects;

        private void Awake() => Rebuild();

        // Recompose resolved stats + effect list. Called on Awake; call again when
        // a stat/type input changes (perk/upgrade — later). This is the
        // invalidation hook that keeps the immutable StatBlock fresh.
        public void Rebuild()
        {
            if (weapon == null)
            {
                Debug.LogError("[WeaponDamageSource] No WeaponSO assigned.");
                cachedStats = default;
                cachedEffects = new List<IHitEffect>();
                return;
            }

            // resolve stats: archetype base + weapon deltas
            cachedStats = weapon.ResolveStats();

            // build effect list: GENERATED base damage first, then riders
            cachedEffects = new List<IHitEffect>(weapon.riderEffects.Count + 1);

            // synthesized base-damage effect: 100% of weapon damage, weapon's type.
            // Behaves identically to the old hand-placed DamageEffectSO base damage.
            var baseSpec = new DamageSpec(
                DamageDerivation.PercentOfWeapon,
                1f,
                weapon.baseDamageType,
                DerivationTiming.SnapshotAtApply);
            cachedEffects.Add(new DamageHitEffect(baseSpec));

            // riders
            foreach (var def in weapon.riderEffects)
                if (def != null)
                    cachedEffects.Add(def.GetInstance());
        }

        // ---- IDamageSource ----
        public StatBlock GetStats() => cachedStats;
        public List<IHitEffect> GetEffects() => cachedEffects;
        public int Faction => weapon != null ? weapon.faction : 0;
        public DamageTypeSO BaseDamageType => weapon != null ? weapon.baseDamageType : null;
        public int MaxChainDepth => weapon != null ? weapon.maxChainDepth : 0;
        public float ChainFalloff => weapon != null ? weapon.chainFalloff : 1f;
        public float ChainGrowth => weapon != null ? weapon.chainGrowth : 1f;
        public HitDedupMode DedupMode => weapon != null ? weapon.dedupMode : HitDedupMode.PerShot;

        // ---- IProjectileSource ----
        public ProjectileConfig GetProjectileConfig() => projectileConfig;

        // ---- Accessors for the firing driver ----
        public WeaponSO Weapon => weapon;
        public float ResolvedRPM => cachedStats.RoundsPerMinute;
        public AudioClip FireClip => weapon != null ? weapon.fireClip : null;
        public AudioClip EmptyClip => weapon != null ? weapon.emptyClip : null;

        // ---- Upgrade hooks ----
        // Rider effects now live on the WeaponSO; runtime add/remove would layer
        // over that (perk/upgrade phase). Projectile pierce hook kept.
        public void AddPierce(int amount) => projectileConfig.AddPierce(amount);
    }
}