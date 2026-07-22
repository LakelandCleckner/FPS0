using System.Collections.Generic;
using UnityEngine;
using Combat.Core;

namespace Combat.Delivery
{
    // Raycast-per-frame kinematic projectile. Carries the attacker (ICombatant) and
    // source (IDamageSource), stamped onto the hit context. Target is the hitbox's
    // combatant (ICombatant). No DamageStats snapshot (base damage derives live).
    //
    // POOLED. Despawn returns to ProjectilePool instead of destroying, so sustained
    // auto-fire no longer instantiates and collects a GameObject per shot.
    //
    // Everything that varies per flight MUST be reset in Init — field initialisers
    // run once per instance, not once per spawn, and a missed reset gives you a
    // projectile that despawns instantly (stale age) or refuses to hit an enemy it
    // struck in a previous life (stale hitTargets). Same failure class as the
    // unreset WasCrit.
    public class Projectile : MonoBehaviour
    {
        private WeaponHitResolver resolver;
        private Combat.Sources.IDamageSource damageSource;
        private ICombatant attacker;
        private List<IHitEffect> effects;
        private int sourceFaction;
        private DamageTypeSO damageType;
        private int maxChainDepth;
        private float chainFalloff;
        private float chainGrowth;
        private HitDedupMode dedupMode;
        private ProjectileConfig config;

        private Vector3 direction;
        private float distanceTravelled;
        private float age;
        private int pierceUsed;

        private readonly HashSet<ICombatant> hitTargets = new HashSet<ICombatant>();
        private bool initialized;

        // Which prefab this instance came from, so it can return to the right queue.
        private Projectile poolOrigin;

        // Reused across every hit this projectile makes AND every flight it's pooled
        // for. Refilled in HandleHit; BumpGeneration makes a retained reference fail
        // loudly rather than silently reading a later hit.
        private HitContext hitContext;

        // The hitbox currently being resolved. Read by the cached base-damage
        // delegate below rather than captured in a per-hit closure.
        private EnemyHitbox currentHitbox;

        // Allocated once per projectile instead of once per hit. Safe to bind to
        // mutable fields because ApplyDamageToTarget is consumed synchronously during
        // resolution and never retained — unlike ApplyStatusTickDamage, which
        // EffectStackPool holds for the life of a status and which therefore lives on
        // EnemyHitbox instead.
        private System.Action<float> applyBaseDamage;

        private void Awake()
        {
            hitContext = new HitContext();
            applyBaseDamage = ApplyBaseDamage;
        }

        private void ApplyBaseDamage(float dmg)
        {
            if (currentHitbox == null) return;
            currentHitbox.combatant.TakeDamage(dmg, currentHitbox.bodyPart, damageType);
        }

        public void SetPoolOrigin(Projectile prefab) => poolOrigin = prefab;

        public void Init(
            WeaponHitResolver resolver,
            Combat.Sources.IDamageSource damageSource,
            ICombatant attacker,
            List<IHitEffect> effects,
            int sourceFaction,
            DamageTypeSO damageType,
            int maxChainDepth,
            float chainFalloff,
            float chainGrowth,
            HitDedupMode dedupMode,
            ProjectileConfig config,
            Vector3 direction,
            in ShotInfo shot)
        {
            this.resolver = resolver;
            this.damageSource = damageSource;
            this.attacker = attacker;
            this.effects = effects;
            this.sourceFaction = sourceFaction;
            this.damageType = damageType;
            this.maxChainDepth = maxChainDepth;
            this.chainFalloff = chainFalloff;
            this.chainGrowth = chainGrowth;
            this.dedupMode = dedupMode;
            this.config = config;
            this.direction = direction.normalized;

            // PER-FLIGHT RESET — required for pooling. Previously implicit in being a
            // freshly instantiated object.
            distanceTravelled = 0f;
            age = 0f;
            pierceUsed = 0;
            hitTargets.Clear();
            currentHitbox = null;

            // Shot-level context fields. The per-hit ones are set in HandleHit.
            hitContext.Source = HitSource.Direct;
            hitContext.DamageSource = damageSource;
            hitContext.Attacker = attacker;
            hitContext.SourceFaction = sourceFaction;
            hitContext.DamageType = damageType;
            hitContext.Effects = effects;
            hitContext.MaxChainDepth = maxChainDepth;
            hitContext.ChainFalloff = chainFalloff;
            hitContext.ChainGrowth = chainGrowth;
            hitContext.DedupMode = dedupMode;
            hitContext.ApplyDamageToTarget = applyBaseDamage;
            hitContext.Shot = shot;

            initialized = true;
        }

        private void Update()
        {
            if (!initialized) return;

            float step = config.speed * Time.deltaTime;
            Vector3 start = transform.position;

            if (Physics.Raycast(start, direction, out var hit, step, config.collisionMask))
            {
                HandleHit(hit);
                if (!initialized) return;
                transform.position = hit.point;
            }
            else
            {
                transform.position = start + direction * step;
            }

            distanceTravelled += step;
            age += Time.deltaTime;

            if (age >= config.maxLifetime || distanceTravelled >= config.maxDistance)
                Despawn();
        }

        private void HandleHit(RaycastHit hit)
        {
            var hitbox = hit.collider.GetComponentInParent<EnemyHitbox>();

            if (hitbox == null)
            {
                if (config.stopOnEnvironment)
                    Despawn();
                return;
            }

            var target = hitbox.combatant as ICombatant;
            if (target == null) return;

            if (hitTargets.Contains(target))
                return;
            hitTargets.Add(target);

            currentHitbox = hitbox;

            // Refill the reusable context. Every field an effect or the resolver
            // WRITES must be reset here or state leaks from the previous hit —
            // CritMultiplier and WasCrit are reset by RollCrit before any effect runs.
            hitContext.BumpGeneration();

            hitContext.Target = target;
            hitContext.HitPoint = hit.point;
            hitContext.HitboxMultiplier = hitbox.damageMultiplier;
            hitContext.BodyPartHit = hitbox.bodyPart;

            // Cached on the hitbox: no allocation, and safe to retain, which matters
            // because EffectStackPool holds this delegate for the life of a status.
            hitContext.ApplyStatusTickDamage = hitbox.ApplyTickDamage;

            hitContext.DamageDealt = 0f;
            hitContext.WasKill = false;
            hitContext.WasHeadshot = false;
            hitContext.WasDebuffed = false;
            hitContext.ChainDepth = 0;
            hitContext.ResetAlreadyHit();

            resolver.ResolveHit(hitContext);

            switch (config.impactMode)
            {
                case ProjectileImpactMode.DestroyOnHit:
                    Despawn();
                    break;
                case ProjectileImpactMode.Pierce:
                    pierceUsed++;
                    if (pierceUsed > config.maxPierceCount)
                        Despawn();
                    break;
                case ProjectileImpactMode.Embed:
                    Despawn();
                    break;
            }
        }

        private void Despawn()
        {
            initialized = false;
            currentHitbox = null;

            if (ProjectilePool.Instance != null)
                ProjectilePool.Instance.Return(this, poolOrigin);
            else
                Destroy(gameObject);
        }
    }
}