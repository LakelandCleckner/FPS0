using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Weapons;
using Combat.Stats;

namespace Combat.Sources
{
    // Runtime weapon. Resolves weapon-home stats through a StatContainer. Phase 2g:
    //  - the DamageStats snapshot is CACHE-INVALIDATED off the container's version
    //    (runtime stat changes -> next GetStats() rebuilds once), not frozen at Awake
    //  - exposes the WIELDER's player-scope stats (CombatantStats) so the delivery
    //    can stamp them onto the hit context (AttackerStats)
    public class WeaponDamageSource : MonoBehaviour, IDamageSource
    {
        [Header("Weapon Definition")]
        [SerializeField] private WeaponSO weapon;

        [Header("Stat Keys")]
        [SerializeField] private WeaponStatKeys statKeys;

        [Header("Wielder (player-scope stats: crit, global damage)")]
        [Tooltip("The combatant wielding this weapon. Assign the player's " +
                 "CombatantStats. Optional — a sourceless weapon leaves it null.")]
        [SerializeField] private CombatantStats wielderStats;

        private StatContainer container;

        // cached snapshot + the weapon_damage version it was built at
        private DamageStats cachedStats;
        private int cachedStatsVersion = int.MinValue;

        private List<IHitEffect> cachedEffects;

        public StatContainer Container => container;

        // The wielder's player-scope container (nullable). Delivery stamps this onto
        // the hit context as AttackerStats.
        public StatContainer AttackerStats => wielderStats != null ? wielderStats.Container : null;

        private void Awake()
        {
            // fallback: find the wielder up the hierarchy once if not assigned
            if (wielderStats == null)
                wielderStats = GetComponentInParent<CombatantStats>();
            Rebuild();
        }

        // Build the container + effect list. Call when the weapon/effect SET changes
        // (equip, upgrade that adds/removes riders). Runtime STAT-VALUE changes don't
        // need a full Rebuild — the snapshot self-invalidates off the container
        // version (see GetStats).
        public void Rebuild()
        {
            if (weapon == null || statKeys == null)
            {
                Debug.LogError("[WeaponDamageSource] Missing WeaponSO or WeaponStatKeys.");
                cachedEffects = new List<IHitEffect>();
                return;
            }

            container ??= new StatContainer();
            WeaponStatBuilder.PopulateBases(container, weapon, statKeys);

            // force the snapshot to rebuild on next GetStats
            cachedStatsVersion = int.MinValue;

            cachedEffects = new List<IHitEffect>(weapon.riderEffects.Count + 1);
            var baseSpec = new DamageSpec(
                DamageDerivation.PercentOfWeapon, 1f,
                weapon.baseDamageType, DerivationTiming.SnapshotAtApply);
            cachedEffects.Add(new DamageHitEffect(baseSpec));

            foreach (var def in weapon.riderEffects)
                if (def != null)
                    cachedEffects.Add(def.GetInstance());
        }

        // ---- IDamageSource ----

        // Returns the current resolved snapshot. Cached, but INVALIDATED when
        // weapon_damage's container version changes (runtime modifier added/removed,
        // base changed) — so runtime stat changes are reflected on the next call,
        // without resolving when nothing changed. A caller that copies this struct
        // (projectile/DOT) freezes its value at that moment.
        public DamageStats GetStats()
        {
            if (container == null || statKeys == null) return cachedStats;

            int v = container.GetVersion(statKeys.weaponDamage);
            if (v != cachedStatsVersion)
            {
                float baseDamage = container.Resolve(statKeys.weaponDamage);
                cachedStats = new DamageStats(baseDamage);
                cachedStatsVersion = v;
            }
            return cachedStats;
        }

        public List<IHitEffect> GetEffects() => cachedEffects;
        public int Faction => weapon != null ? weapon.faction : 0;
        public DamageTypeSO BaseDamageType => weapon != null ? weapon.baseDamageType : null;
        public int MaxChainDepth => weapon != null ? weapon.maxChainDepth : 0;
        public float ChainFalloff => weapon != null ? weapon.chainFalloff : 1f;
        public float ChainGrowth => weapon != null ? weapon.chainGrowth : 1f;
        public HitDedupMode DedupMode => weapon != null ? weapon.dedupMode : HitDedupMode.PerShot;

        // ---- Resolved weapon stats for the fire controller / ammo (live, cached) ----
        public float ResolvedRPM => container != null && statKeys != null ? container.Resolve(statKeys.rpm) : 0f;
        public float ResolvedMagazineSize => container != null && statKeys != null ? container.Resolve(statKeys.magazineSize) : 0f;
        public float ResolvedReloadTime => container != null && statKeys != null ? container.Resolve(statKeys.reloadTime) : 0f;

        public WeaponSO Weapon => weapon;
        public AudioClip FireClip => weapon != null ? weapon.fireClip : null;
        public AudioClip EmptyClip => weapon != null ? weapon.emptyClip : null;
    }
}