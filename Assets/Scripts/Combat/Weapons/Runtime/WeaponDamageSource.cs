using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Weapons;

namespace Combat.Sources
{
    // Runtime weapon: the live gun in-hand. References a WeaponSO and resolves
    // stats + generates base damage from the SO hierarchy.
    //
    // PHASE 1b: projectile config + delivery moved OUT of here into the fire-mode
    // delivery SOs. This no longer implements IProjectileSource or holds a
    // ProjectileConfig — the delivery (built from data) owns projectile params.
    public class WeaponDamageSource : MonoBehaviour, IDamageSource
    {
        [Header("Weapon Definition")]
        [SerializeField] private WeaponSO weapon;

        private StatBlock cachedStats;
        private List<IHitEffect> cachedEffects;

        private void Awake() => Rebuild();

        // Recompose resolved stats + effect list (base damage + riders). Call again
        // when a stat/type input changes (perk/upgrade — later).
        public void Rebuild()
        {
            if (weapon == null)
            {
                Debug.LogError("[WeaponDamageSource] No WeaponSO assigned.");
                cachedStats = default;
                cachedEffects = new List<IHitEffect>();
                return;
            }

            cachedStats = weapon.ResolveStats();

            cachedEffects = new List<IHitEffect>(weapon.riderEffects.Count + 1);

            // synthesized base-damage effect: 100% of weapon damage, weapon's type
            var baseSpec = new DamageSpec(
                DamageDerivation.PercentOfWeapon,
                1f,
                weapon.baseDamageType,
                DerivationTiming.SnapshotAtApply);
            cachedEffects.Add(new DamageHitEffect(baseSpec));

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

        // ---- Accessors for the fire controller ----
        public WeaponSO Weapon => weapon;
        public AudioClip FireClip => weapon != null ? weapon.fireClip : null;
        public AudioClip EmptyClip => weapon != null ? weapon.emptyClip : null;
    }
}
