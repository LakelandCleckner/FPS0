using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Weapons;
using Combat.Stats;

namespace Combat.Sources
{
    // Runtime weapon. Base damage is PercentOfStat(weapon_damage, Source) resolved
    // live from the container. Exposes the wielder as an ICombatant (now CombatantStats,
    // so crit reads go straight to the stat container -- no health coupling) and its
    // own SourceStats.
    public class WeaponDamageSource : MonoBehaviour, IDamageSource, IRangeFalloffSource
    {
        [Header("Weapon Definition")]
        [SerializeField] private WeaponSO weapon;

        [Header("Stat Keys")]
        [SerializeField] private WeaponStatKeys statKeys;

        [Header("Wielder (the combatant: CombatantStats)")]
        [Tooltip("The combatant wielding this weapon -- assign the player's CombatantStats. " +
                 "Optional; a sourceless weapon leaves it null.")]
        [SerializeField] private CombatantStats wielder;

        private StatContainer container;
        private List<IHitEffect> cachedEffects;
        private readonly List<IHitEffect> perkEffects = new List<IHitEffect>();
        private WeaponInstance instance;

        public StatContainer Container => container;
        public StatContainer SourceStats => container;
        public ICombatant Attacker => wielder;   // CombatantStats implements ICombatant
        public WeaponInstance Instance => instance;

        // Binds this runtime to an owned weapon. Call while inactive, before Awake.
        public void Initialize(WeaponInstance owned)
        {
            instance = owned;
            weapon = owned != null ? owned.Definition : null;
        }

        private void Awake()
        {
            if (wielder == null)
                wielder = GetComponentInParent<CombatantStats>();
            Rebuild();
        }

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

            cachedEffects = new List<IHitEffect>(weapon.riderEffects.Count + 1);

            var baseSpec = new DamageSpec(
                statKeys.weaponDamage, StatScope.Source, 1f, weapon.baseDamageType);
            cachedEffects.Add(new DamageHitEffect(baseSpec));

            foreach (var def in weapon.riderEffects)
                if (def != null)
                    cachedEffects.Add(def.GetInstance());

            cachedEffects.AddRange(perkEffects);
        }

        // On-hit effects added by perks. Deliveries read the list each shot, so changes apply next shot.
        public void AddEffect(IHitEffect effect)
        {
            if (effect == null) return;
            perkEffects.Add(effect);
            if (cachedEffects != null) cachedEffects.Add(effect);
        }

        public void RemoveEffect(IHitEffect effect)
        {
            if (effect == null) return;
            perkEffects.Remove(effect);
            if (cachedEffects != null) cachedEffects.Remove(effect);
        }

        public List<IHitEffect> GetEffects() => cachedEffects;
        public int Faction => weapon != null ? weapon.faction : 0;
        public DamageTypeSO BaseDamageType => weapon != null ? weapon.baseDamageType : null;
        public int MaxChainDepth => weapon != null ? weapon.maxChainDepth : 0;
        public float ChainFalloff => weapon != null ? weapon.chainFalloff : 1f;
        public float ChainGrowth => weapon != null ? weapon.chainGrowth : 1f;
        public HitDedupMode DedupMode => weapon != null ? weapon.dedupMode : HitDedupMode.PerShot;

        public float ResolvedRPM => container != null && statKeys != null ? container.Resolve(statKeys.rpm) : 0f;
        public float ResolvedMagazineSize => container != null && statKeys != null ? container.Resolve(statKeys.magazineSize) : 0f;
        public float ResolvedReloadTime => container != null && statKeys != null ? container.Resolve(statKeys.reloadTime) : 0f;

        public float ResolvedEquipTime => container != null && statKeys != null && statKeys.equipTime != null ? container.Resolve(statKeys.equipTime) : 0f;

        public float ResolvedAdsTime => container != null && statKeys != null && statKeys.adsTime != null ? container.Resolve(statKeys.adsTime) : 0.25f;
        public float ResolvedAdsZoom => container != null && statKeys != null && statKeys.adsZoom != null ? container.Resolve(statKeys.adsZoom) : 1.2f;
        public float ResolvedPelletSpread => container != null && statKeys != null && statKeys.pelletSpread != null ? container.Resolve(statKeys.pelletSpread) : 1f;

        // Range stat, hard-capped 0-100
        public float ResolvedRange
        {
            get
            {
                float r = container != null && statKeys != null && statKeys.range != null
                    ? container.Resolve(statKeys.range)
                    : (weapon != null && weapon.archetype != null ? weapon.archetype.range + weapon.rangeDelta : 50f);
                return Mathf.Clamp(r, 0f, 100f);
            }
        }

        // Aim assist stat, hard-capped 0-100
        public float ResolvedAimAssist
        {
            get
            {
                float v = container != null && statKeys != null && statKeys.aimAssist != null
                    ? container.Resolve(statKeys.aimAssist)
                    : (weapon != null && weapon.archetype != null ? weapon.archetype.aimAssist + weapon.aimAssistDelta : 0f);
                return Mathf.Clamp(v, 0f, 100f);
            }
        }

        // Magnetism cone half-angle (deg), hip -> ADS
        public float AssistAngle(float aimBlend)
        {
            var a = weapon != null ? weapon.archetype : null;
            if (a == null) return 0f;
            float ads = Mathf.Lerp(a.assistAngleAtMin, a.assistAngleAtMax, ResolvedAimAssist / 100f);
            return ads * Mathf.Lerp(a.assistHipMultiplier, 1f, Mathf.Clamp01(aimBlend));
        }

        // Magnetism reach, tied to falloff end
        public float AssistMaxDistance(float aimBlend)
        {
            var a = weapon != null ? weapon.archetype : null;
            if (a == null) return 0f;
            return WeaponFalloff.End(a, ResolvedRange, aimBlend) * a.assistDistanceMultiplier;
        }

        public float FalloffStart(float aimBlend)
            => WeaponFalloff.Start(weapon != null ? weapon.archetype : null, ResolvedRange, aimBlend);

        public float FalloffMultiplier(float distance, float aimBlend)
            => WeaponFalloff.Multiplier(weapon != null ? weapon.archetype : null, ResolvedRange, distance, aimBlend);

        public WeaponSO Weapon => weapon;
        public AudioClip FireClip => weapon != null ? weapon.fireClip : null;
        public AudioClip EmptyClip => weapon != null ? weapon.emptyClip : null;
    }
}