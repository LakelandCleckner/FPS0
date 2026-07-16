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
    // so crit reads go straight to the stat container — no health coupling) and its
    // own SourceStats.
    public class WeaponDamageSource : MonoBehaviour, IDamageSource
    {
        [Header("Weapon Definition")]
        [SerializeField] private WeaponSO weapon;

        [Header("Stat Keys")]
        [SerializeField] private WeaponStatKeys statKeys;

        [Header("Wielder (the combatant: CombatantStats)")]
        [Tooltip("The combatant wielding this weapon — assign the player's CombatantStats. " +
                 "Optional; a sourceless weapon leaves it null.")]
        [SerializeField] private CombatantStats wielder;

        private StatContainer container;
        private List<IHitEffect> cachedEffects;

        public StatContainer Container => container;
        public StatContainer SourceStats => container;
        public ICombatant Attacker => wielder;   // CombatantStats implements ICombatant

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

        public WeaponSO Weapon => weapon;
        public AudioClip FireClip => weapon != null ? weapon.fireClip : null;
        public AudioClip EmptyClip => weapon != null ? weapon.emptyClip : null;
    }
}