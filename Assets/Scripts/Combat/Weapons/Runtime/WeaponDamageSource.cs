using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Weapons;
using Combat.Stats;

namespace Combat.Sources
{
    // Runtime weapon: the live gun in-hand. References a WeaponSO and now resolves
    // its stats through the STAT SYSTEM (a StatContainer of weapon-home stats),
    // replacing the retired StatBlock/ResolveStats.
    //
    // Weapon-home stats (weapon_damage, rpm, magazine_size, reload_time) live in the
    // container, populated from archetype base + weapon deltas. Base damage for the
    // damage pipeline is DamageStats(resolved weapon_damage). RPM / magazine / reload
    // are read from the container by the fire controller and ammo system.
    //
    // Player-scope stats (crit, global damage) are NOT here — they live on the
    // player and reach a hit at resolve time (Phase 2g/2h). Weapon affixes that
    // buff player stats are modifier sources targeting the player's container (later).
    public class WeaponDamageSource : MonoBehaviour, IDamageSource
    {
        [Header("Weapon Definition")]
        [SerializeField] private WeaponSO weapon;

        [Header("Stat Keys")]
        [Tooltip("References to the weapon stat definitions (weapon_damage, rpm, etc.).")]
        [SerializeField] private WeaponStatKeys statKeys;

        private StatContainer container;
        private DamageStats cachedStats;
        private List<IHitEffect> cachedEffects;

        public StatContainer Container => container;

        private void Awake() => Rebuild();

        // Recompose the weapon's stat container + snapshot + effect list. Call again
        // when a stat input changes (perk/upgrade — later).
        public void Rebuild()
        {
            if (weapon == null || statKeys == null)
            {
                Debug.LogError("[WeaponDamageSource] Missing WeaponSO or WeaponStatKeys.");
                cachedStats = default;
                cachedEffects = new List<IHitEffect>();
                return;
            }

            // build/populate the weapon's stat container (archetype base + deltas)
            container ??= new StatContainer();
            WeaponStatBuilder.PopulateBases(container, weapon, statKeys);

            // snapshot the resolved base damage for the damage pipeline
            float baseDamage = container.Resolve(statKeys.weaponDamage);
            cachedStats = new DamageStats(baseDamage);

            // build effect list: generated base damage + riders
            cachedEffects = new List<IHitEffect>(weapon.riderEffects.Count + 1);
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
        public DamageStats GetStats() => cachedStats;
        public List<IHitEffect> GetEffects() => cachedEffects;
        public int Faction => weapon != null ? weapon.faction : 0;
        public DamageTypeSO BaseDamageType => weapon != null ? weapon.baseDamageType : null;
        public int MaxChainDepth => weapon != null ? weapon.maxChainDepth : 0;
        public float ChainFalloff => weapon != null ? weapon.chainFalloff : 1f;
        public float ChainGrowth => weapon != null ? weapon.chainGrowth : 1f;
        public HitDedupMode DedupMode => weapon != null ? weapon.dedupMode : HitDedupMode.PerShot;

        // ---- Resolved weapon stats for the fire controller / ammo ----
        // (These read the container so RPM/magazine/reload come from the stat system.)
        public float ResolvedRPM => container != null && statKeys != null ? container.Resolve(statKeys.rpm) : 0f;
        public float ResolvedMagazineSize => container != null && statKeys != null ? container.Resolve(statKeys.magazineSize) : 0f;
        public float ResolvedReloadTime => container != null && statKeys != null ? container.Resolve(statKeys.reloadTime) : 0f;

        // ---- Accessors for the fire controller ----
        public WeaponSO Weapon => weapon;
        public AudioClip FireClip => weapon != null ? weapon.fireClip : null;
        public AudioClip EmptyClip => weapon != null ? weapon.emptyClip : null;
    }
}