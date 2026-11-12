using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.AI;
using Combat.Core;
using Combat.Delivery;
using Combat.Effects;
using Combat.Sources;
using Combat.Stats;
using Combat.Spawning;
using Combat.Weapons;

namespace Combat.Enemies
{
    // Bursts at the believed target position through a DeliverySO; aim settles over time on target, raising the weapon is the wind-up.
    public class EnemyRangedAttack : MonoBehaviour, IDamageSource, IPoolable
    {
        public enum Phase { Lowered, Raising, Ready, Bursting, Reloading }

        [Header("References")]
        [SerializeField] private CombatantStats combatantStats;
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private Animator animator;
        [SerializeField] private EnemyRangedStatKeys statKeys;
        [Tooltip("Hitscan or projectile. Projectile deliveries spawn straight from the pool so ranged_projectile_speed applies.")]
        [SerializeField] private DeliverySO delivery;
        [Tooltip("Shot origin when there is no EnemyWeaponModel; with one, the gun's own muzzle is used.")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private DamageTypeSO damageType;

        [Header("Base Stats (pushed as stat bases at Start)")]
        [SerializeField] private float baseDamage = 10f;
        [SerializeField] private float baseFireRate = 420f;
        [SerializeField] private float baseBurstSize = 3f;
        [SerializeField] private float baseBurstSeparation = 1.4f;
        [SerializeField] private float baseAimError = 6f;
        [SerializeField] private float baseAimErrorMin = 1.5f;
        [SerializeField] private float baseAimSettleTime = 3f;
        [SerializeField] private float baseBurstSpread = 1.2f;
        [SerializeField] private float baseTargetDelay = 0.15f;
        [SerializeField] private float baseTracking = 0.5f;
        [SerializeField] private float baseLeading = 0.6f;
        [SerializeField] private float baseProjectileSpeed = 1f;
        [SerializeField] private float baseMaxRange = 30f;
        [SerializeField] private float baseMagazine = 24f;
        [SerializeField] private float baseReloadTime = 2.2f;
        [SerializeField] private float baseWindup = 0.35f;

        [Header("Aim")]
        [Tooltip("Height on the target the aim point sits at (m).")]
        [SerializeField] private float targetAimHeight = 1.2f;
        [Tooltip("Max angle between facing and the target before a burst may start (deg).")]
        [SerializeField] private float maxStartAngle = 25f;
        [Tooltip("Seconds out of sight before the aim settle starts over.")]
        [SerializeField] private float settleLossTime = 0.5f;

        [Header("Animator")]
        [SerializeField] private string aimingBool = "Aiming";
        [SerializeField] private string fireTrigger = "Fire";
        [SerializeField] private string reloadTrigger = "Reload";
        [SerializeField] private string reloadSpeedFloat = "ReloadSpeed";
        [SerializeField] private string flinchTrigger = "Flinch";
        [Tooltip("Length of the reload clip at speed 1; the reload state is sped up to fit ranged_reload_time.")]
        [SerializeField] private float reloadClipLength = 2.2f;

        [Header("Debug")]
        [SerializeField] private bool logShots = false;

        public event Action Flinched;

        public Phase CurrentPhase { get; private set; }
        public bool Engaged { get; private set; }
        public bool IsReloading => CurrentPhase == Phase.Reloading;
        public int Rounds { get; private set; }
        public int MagazineSize => Mathf.RoundToInt(Stat(statKeys != null ? statKeys.magazine : null, baseMagazine));
        public bool NeedsReload => MagazineSize > 0 && Rounds <= 0 && !IsReloading;
        public float MaxRange => Stat(statKeys != null ? statKeys.maxRange : null, baseMaxRange);
        public float AimCone { get; private set; }
        public float SettleProgress => Mathf.Clamp01(settle / Mathf.Max(0.01f, AimSettleTime));
        public Vector3 LastAimPoint { get; private set; }
        public Transform Muzzle => muzzle;

        private float FireRate => Mathf.Max(1f, Stat(statKeys != null ? statKeys.fireRate : null, baseFireRate));
        private int BurstSize => Mathf.Max(1, Mathf.RoundToInt(Stat(statKeys != null ? statKeys.burstSize : null, baseBurstSize)));
        private float BurstSeparation => Mathf.Max(0f, Stat(statKeys != null ? statKeys.burstSeparation : null, baseBurstSeparation));
        private float AimError => Mathf.Max(0f, Stat(statKeys != null ? statKeys.aimError : null, baseAimError));
        private float AimErrorMin => Mathf.Max(0f, Stat(statKeys != null ? statKeys.aimErrorMin : null, baseAimErrorMin));
        private float AimSettleTime => Mathf.Max(0f, Stat(statKeys != null ? statKeys.aimSettleTime : null, baseAimSettleTime));
        private float BurstSpread => Mathf.Max(0f, Stat(statKeys != null ? statKeys.burstSpread : null, baseBurstSpread));
        private float TargetDelay => Mathf.Max(0f, Stat(statKeys != null ? statKeys.targetDelay : null, baseTargetDelay));
        private float Tracking => Mathf.Clamp01(Stat(statKeys != null ? statKeys.tracking : null, baseTracking));
        private float Leading => Mathf.Clamp01(Stat(statKeys != null ? statKeys.leading : null, baseLeading));
        private float ProjectileSpeedScale => Mathf.Max(0.05f, Stat(statKeys != null ? statKeys.projectileSpeed : null, baseProjectileSpeed));
        private float ReloadTime => Mathf.Max(0.05f, Stat(statKeys != null ? statKeys.reloadTime : null, baseReloadTime));
        private float Windup => Mathf.Max(0f, Stat(statKeys != null ? statKeys.windup : null, baseWindup));

        private EnemyPerception perception;
        private EnemyBrain brain;
        private CombatantHealth health;
        private EnemyDefinitionApplier definition;
        private EnemyBehaviourProfileSO profile;
        private IDelivery hitscanDelivery;
        private ProjectileDeliverySO projectileDelivery;
        private readonly List<IHitEffect> effects = new List<IHitEffect>(2);

        private float settle;
        private float lastVisibleTime = float.NegativeInfinity;
        private bool wasVisible;
        private float targetDelayUntil;
        private float raisedAt;
        private float cooldownUntil;
        private float flinchUntil;
        private float reloadUntil;
        private float nextShotAt;
        private int shotsLeft;
        private int burstIndex;
        private int burstCount;
        private int shotId;
        private Vector3 burstAim;
        private bool aiming;
        private float lastHealth = -1f;
        private float flinchDamage;
        private float flinchWindowStart;

        private int aimingHash, fireHash, reloadHash, reloadSpeedHash, flinchHash;
        private bool hasAiming, hasFire, hasReload, hasReloadSpeed, hasFlinch;

        private const float FlinchWindow = 0.5f;

        // ---- IDamageSource ----
        public List<IHitEffect> GetEffects() => effects;
        public int Faction => combatantStats != null ? combatantStats.Faction : 0;
        public DamageTypeSO BaseDamageType => damageType;
        public int MaxChainDepth => 0;
        public float ChainFalloff => 1f;
        public float ChainGrowth => 1f;
        public HitDedupMode DedupMode => HitDedupMode.PerShot;
        public ICombatant Attacker => combatantStats;
        public StatContainer SourceStats => combatantStats != null ? combatantStats.Container : null;

        private void Awake()
        {
            if (combatantStats == null) combatantStats = GetComponent<CombatantStats>();
            if (resolver == null) resolver = GetComponentInChildren<WeaponHitResolver>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            perception = GetComponent<EnemyPerception>();
            brain = GetComponent<EnemyBrain>();
            health = GetComponent<CombatantHealth>();
            definition = GetComponent<EnemyDefinitionApplier>();
            var weaponModel = GetComponentInChildren<EnemyWeaponModel>();
            if (weaponModel != null && weaponModel.EnsureModel() != null && weaponModel.Muzzle != null) muzzle = weaponModel.Muzzle;
            CacheAnimatorParams();
            BuildDelivery();
            BuildEffects();
        }

        private void Start()
        {
            PushBases();
            Rounds = MagazineSize;
        }

        private void OnEnable()
        {
            if (health != null) health.HealthChanged += HandleHealthChanged;
        }

        private void OnDisable()
        {
            if (health != null) health.HealthChanged -= HandleHealthChanged;
            Disengage();
        }

        public void OnSpawn()
        {
            Engaged = false;
            CurrentPhase = Phase.Lowered;
            Rounds = MagazineSize;
            settle = 0f;
            wasVisible = false;
            lastVisibleTime = float.NegativeInfinity;
            targetDelayUntil = cooldownUntil = flinchUntil = reloadUntil = nextShotAt = 0f;
            shotsLeft = 0;
            lastHealth = -1f;
            flinchDamage = 0f;
            SetAiming(false);
        }

        public void OnDespawn() => Disengage();

        // Behaviour tuning (aim growth, jitter, flinch, lowering) comes from the profile.
        public void ApplyProfile(EnemyBehaviourProfileSO p) => profile = p;

        // ---- API for actions ----

        // Allowed to raise the weapon and fire at the target until Disengage.
        public void Engage() => Engaged = true;

        public void Disengage()
        {
            Engaged = false;
            if (CurrentPhase == Phase.Bursting) EndBurst();
        }

        public bool StartReload()
        {
            if (IsReloading || MagazineSize <= 0) return false;
            if (CurrentPhase == Phase.Bursting) EndBurst();
            CurrentPhase = Phase.Reloading;
            reloadUntil = Time.time + ReloadTime;
            if (animator != null)
            {
                if (hasReloadSpeed) animator.SetFloat(reloadSpeedHash, reloadClipLength / ReloadTime);
                if (hasReload) animator.SetTrigger(reloadHash);
            }
            return true;
        }

        // Moved, staggered or lost the target: accuracy starts over
        public void ResetAim() => settle = 0f;

        // ---- frame ----

        private void Update()
        {
            if (combatantStats != null && combatantStats.IsDying) { Disengage(); SetAiming(false); return; }

            float now = Time.time;
            bool visible = perception != null && perception.HasTarget && perception.TargetVisible;
            UpdateSight(visible, now);
            UpdateCone(visible);

            if (CurrentPhase == Phase.Reloading)
            {
                if (now >= reloadUntil) { Rounds = MagazineSize; CurrentPhase = aiming ? Phase.Ready : Phase.Lowered; }
                return;
            }

            float lowerAfter = profile != null ? profile.lowerAfter : 2.5f;
            bool wantAim = Engaged && perception != null && perception.HasTarget && now - lastVisibleTime <= lowerAfter;
            SetAiming(wantAim);
            if (!aiming) { if (CurrentPhase == Phase.Bursting) EndBurst(); CurrentPhase = Phase.Lowered; return; }

            if (CurrentPhase == Phase.Bursting) { TickBurst(now, visible); return; }

            CurrentPhase = now - raisedAt < Windup ? Phase.Raising : Phase.Ready;
            if (CurrentPhase == Phase.Ready && CanStartBurst(now, visible)) StartBurst(now);
        }

        private void UpdateSight(bool visible, float now)
        {
            if (visible)
            {
                // Fresh sight after a gap: reaction delay before the first shot
                if (!wasVisible && now - lastVisibleTime > settleLossTime) targetDelayUntil = now + TargetDelay;
                lastVisibleTime = now;
                settle += Time.deltaTime;
            }
            else if (now - lastVisibleTime > settleLossTime)
            {
                settle = 0f;
            }
            wasVisible = visible;
        }

        // Settled cone plus growth from target speed
        private void UpdateCone(bool visible)
        {
            float t = SettleProgress;
            float cone = Mathf.Lerp(AimError, AimErrorMin, t);
            if (visible && profile != null) cone += perception.TargetVelocity.magnitude * profile.aimErrorPerTargetSpeed;
            AimCone = cone;
        }

        private bool CanStartBurst(float now, bool visible)
        {
            if (!visible || NeedsReload) return false;
            if (now < targetDelayUntil || now < cooldownUntil || now < flinchUntil) return false;
            if (brain != null && !brain.HasRangedToken && !brain.AcquireRangedToken()) return false;

            Vector3 to = perception.BelievedPosition - transform.position;
            to.y = 0f;
            if (to.magnitude > MaxRange) return false;
            return Vector3.Angle(Flat(transform.forward), to) <= maxStartAngle;
        }

        private void StartBurst(float now)
        {
            CurrentPhase = Phase.Bursting;
            burstCount = BurstSize;
            if (MagazineSize > 0) burstCount = Mathf.Min(burstCount, Rounds);
            shotsLeft = burstCount;
            burstIndex = 0;
            burstAim = PredictAimPoint();
            nextShotAt = now;
        }

        private void TickBurst(float now, bool visible)
        {
            if (!visible) { EndBurst(); return; }
            while (shotsLeft > 0 && now >= nextShotAt)
            {
                Fire();
                shotsLeft--;
                burstIndex++;
                nextShotAt += 60f / FireRate;
                if (MagazineSize > 0 && Rounds <= 0) shotsLeft = 0;
            }
            if (shotsLeft <= 0) EndBurst();
        }

        private void EndBurst()
        {
            shotsLeft = 0;
            CurrentPhase = aiming ? Phase.Ready : Phase.Lowered;
            float jitter = profile != null ? profile.burstSeparationJitter : 0f;
            float sep = BurstSeparation * (1f + (jitter > 0f ? UnityEngine.Random.Range(-jitter, jitter) : 0f));
            cooldownUntil = Mathf.Max(cooldownUntil, Time.time + sep);
        }

        // ---- shooting ----

        // Believed position at chest height, led by the target's speed for projectiles
        private Vector3 PredictAimPoint()
        {
            Vector3 p = perception.BelievedPosition + Vector3.up * targetAimHeight;
            float speed = ProjectileSpeed();
            if (speed > 0f && perception.TargetVisible)
            {
                float t = Vector3.Distance(MuzzlePosition, p) / speed;
                p += perception.TargetVelocity * t * Leading;
            }
            return p;
        }

        private void Fire()
        {
            // Follow the target through the burst by the tracking fraction
            burstAim = Vector3.Lerp(burstAim, PredictAimPoint(), Tracking);
            LastAimPoint = burstAim;

            Vector3 origin = MuzzlePosition;
            Vector3 dir = burstAim - origin;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            dir = Scatter(dir.normalized, AimCone);
            if (burstIndex > 0) dir = Scatter(dir, BurstSpread);

            var shot = new ShotInfo(++shotId, burstIndex, burstCount);
            if (projectileDelivery != null) FireProjectile(origin, dir, shot);
            else if (hitscanDelivery != null) hitscanDelivery.Fire(origin, dir, this, shot);

            if (MagazineSize > 0) Rounds--;
            if (animator != null && hasFire) animator.SetTrigger(fireHash);
            if (logShots) Debug.Log($"[EnemyRangedAttack] {name} shot {burstIndex + 1}/{burstCount} cone {AimCone:F1}", this);
        }

        private void FireProjectile(Vector3 origin, Vector3 dir, in ShotInfo shot)
        {
            var so = projectileDelivery.projectile;
            var projectile = ProjectilePool.Instance.Get(so.prefab, origin, Quaternion.LookRotation(dir));
            if (projectile == null) return;
            var config = so.config.Clone();
            config.speed *= ProjectileSpeedScale;
            projectile.Init(resolver, this, combatantStats, effects, Faction, damageType, 0, 1f, 1f,
                            HitDedupMode.PerShot, config, dir, shot);
        }

        // Uniform direction within a cone of this many degrees (full angle)
        private static Vector3 Scatter(Vector3 dir, float coneDegrees)
        {
            if (coneDegrees <= 0f) return dir;
            float half = coneDegrees * 0.5f * Mathf.Deg2Rad;
            Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(half);
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(dir, right);
            return (dir + right * r.x + up * r.y).normalized;
        }

        private float ProjectileSpeed()
        {
            if (projectileDelivery == null || projectileDelivery.projectile == null) return 0f;
            return projectileDelivery.projectile.config.speed * ProjectileSpeedScale;
        }

        private Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.4f;

        // ---- flinch ----

        private void HandleHealthChanged(float current, float max)
        {
            bool damaged = lastHealth >= 0f && current < lastHealth - 0.001f && current > 0f;
            float lost = damaged ? lastHealth - current : 0f;
            lastHealth = current;
            if (!damaged || profile == null || max <= 0f) return;

            if (Time.time - flinchWindowStart > FlinchWindow) { flinchWindowStart = Time.time; flinchDamage = 0f; }
            flinchDamage += lost;
            if (flinchDamage < profile.flinchDamageFraction * max) return;

            flinchDamage = 0f;
            if (CurrentPhase == Phase.Bursting) EndBurst();
            flinchUntil = Time.time + profile.flinchRecovery;
            ResetAim();
            if (animator != null && hasFlinch) animator.SetTrigger(flinchHash);
            Flinched?.Invoke();
        }

        // ---- setup ----

        private void SetAiming(bool on)
        {
            if (on && !aiming) raisedAt = Time.time;
            aiming = on;
            if (animator != null && hasAiming && animator.isActiveAndEnabled) animator.SetBool(aimingHash, on);
        }

        private void BuildDelivery()
        {
            projectileDelivery = delivery as ProjectileDeliverySO;
            if (projectileDelivery != null && (projectileDelivery.projectile == null || projectileDelivery.projectile.prefab == null))
                projectileDelivery = null;
            if (projectileDelivery == null && delivery != null)
                hitscanDelivery = delivery.CreateDelivery(new DeliveryBuildContext(resolver, muzzle));
        }

        private void BuildEffects()
        {
            effects.Clear();
            var stat = statKeys != null ? statKeys.damage : null;
            effects.Add(stat != null
                ? new DamageHitEffect(new DamageSpec(stat, StatScope.Source, 1f, damageType, false))
                : new DamageHitEffect(new DamageSpec(baseDamage, damageType, false)));
        }

        private void CacheAnimatorParams()
        {
            aimingHash = Animator.StringToHash(aimingBool);
            fireHash = Animator.StringToHash(fireTrigger);
            reloadHash = Animator.StringToHash(reloadTrigger);
            reloadSpeedHash = Animator.StringToHash(reloadSpeedFloat);
            flinchHash = Animator.StringToHash(flinchTrigger);
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == aimingHash) hasAiming = true;
                else if (p.nameHash == fireHash) hasFire = true;
                else if (p.nameHash == reloadHash) hasReload = true;
                else if (p.nameHash == reloadSpeedHash) hasReloadSpeed = true;
                else if (p.nameHash == flinchHash) hasFlinch = true;
            }
        }

        // Problems the debug markers list
        public void CollectIssues(List<string> issues)
        {
            if (delivery == null) issues.Add("Ranged: no delivery");
            else if (delivery is ProjectileDeliverySO pd && (pd.projectile == null || pd.projectile.prefab == null))
                issues.Add("Ranged: projectile delivery has no projectile prefab");
            var weaponModel = GetComponentInChildren<EnemyWeaponModel>();
            if (weaponModel != null) weaponModel.CollectIssues(issues);
            if (muzzle == null && (weaponModel == null || !weaponModel.HasMuzzle)) issues.Add("Ranged: no muzzle");
            if (statKeys == null) issues.Add("Ranged: no stat keys (using base values)");
            if (GetComponentInChildren<WeaponHitResolver>() == null) issues.Add("Ranged: no WeaponHitResolver");
            var anim = animator != null ? animator : GetComponentInChildren<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null) { issues.Add("Ranged: no animator controller"); return; }
            if (!Application.isPlaying) return;
            if (!hasAiming) issues.Add($"Animator: no '{aimingBool}' bool");
            if (!hasFire) issues.Add($"Animator: no '{fireTrigger}' trigger");
            if (!hasReload) issues.Add($"Animator: no '{reloadTrigger}' trigger");
        }

        private void PushBases()
        {
            var c = combatantStats != null ? combatantStats.Container : null;
            if (c == null || statKeys == null) return;
            PushBase(c, statKeys.damage, baseDamage);
            PushBase(c, statKeys.fireRate, baseFireRate);
            PushBase(c, statKeys.burstSize, baseBurstSize);
            PushBase(c, statKeys.burstSeparation, baseBurstSeparation);
            PushBase(c, statKeys.aimError, baseAimError);
            PushBase(c, statKeys.aimErrorMin, baseAimErrorMin);
            PushBase(c, statKeys.aimSettleTime, baseAimSettleTime);
            PushBase(c, statKeys.burstSpread, baseBurstSpread);
            PushBase(c, statKeys.targetDelay, baseTargetDelay);
            PushBase(c, statKeys.tracking, baseTracking);
            PushBase(c, statKeys.leading, baseLeading);
            PushBase(c, statKeys.projectileSpeed, baseProjectileSpeed);
            PushBase(c, statKeys.maxRange, baseMaxRange);
            PushBase(c, statKeys.magazine, baseMagazine);
            PushBase(c, statKeys.reloadTime, baseReloadTime);
            PushBase(c, statKeys.windup, baseWindup);
        }

        // Skips stats the enemy definition owns.
        private void PushBase(StatContainer c, StatDefinitionSO def, float value)
        {
            if (def == null) return;
            if (definition != null && definition.DefinesBase(def)) return;
            c.SetBase(def, value);
        }

        private float Stat(StatDefinitionSO def, float fallback)
        {
            var c = combatantStats != null ? combatantStats.Container : null;
            if (c == null || def == null) return fallback;
            return c.Resolve(def);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? MaxRange : baseMaxRange);
            if (muzzle != null) { Gizmos.color = Color.yellow; Gizmos.DrawSphere(muzzle.position, 0.04f); }
        }
#endif
    }
}
