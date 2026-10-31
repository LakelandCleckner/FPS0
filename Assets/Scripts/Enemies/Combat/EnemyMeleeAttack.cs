using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Sources;
using Combat.Stats;
using Combat.Spawning;

namespace Combat.Enemies
{
    // Melee swings that deal damage when the enemy's own hitboxes overlap the player during each clip's damage frames.
    public class EnemyMeleeAttack : MonoBehaviour, IDamageSource, IPoolable
    {
        // One entry per animator state; list index = AttackIndex value (Attack_0, Attack_1, ...).
        [Serializable]
        public class AttackMove
        {
            public string label = "Attack";
            [Min(0f)] public float weight = 1f;
            [Tooltip("Multiplies attack_damage for this move.")]
            public float damageMultiplier = 1f;
            public float clipFrameRate = 60f;
            public int hitStartFrame = 69;
            public int hitEndFrame = 90;
            [Tooltip("Frame the swing ends and the enemy can move again.")]
            public int recoveryEndFrame = 130;
            [Tooltip("Hitboxes that deal damage for this move.")]
            public BodyPart[] hitParts = { BodyPart.Arm };

            [NonSerialized] public List<Collider> Colliders;
            [NonSerialized] public List<IHitEffect> Effects;
        }

        [Header("References")]
        [SerializeField] private CombatantStats combatantStats;
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private Animator animator;
        [SerializeField] private EnemyAttackStatKeys statKeys;
        [SerializeField] private DamageTypeSO damageType;

        [Header("Base Stats (pushed as stat bases at Start)")]
        [SerializeField] private float baseDamage = 80f;
        [SerializeField] private float baseRange = 2.5f;
        [SerializeField] private float baseCooldown = 1.5f;
        [SerializeField] private float baseAttackSpeed = 1f;

        [Header("Moves")]
        [SerializeField] private List<AttackMove> moves = new List<AttackMove> { new AttackMove() };
        [Tooltip("Never pick the same move twice in a row (when there is more than one).")]
        [SerializeField] private bool avoidRepeat = true;

        [Header("Hit Detection")]
        [SerializeField] private LayerMask playerMask = 1 << 8;
        [Tooltip("Extra radius added to each arm collider so fast swings don't skip the player.")]
        [SerializeField] private float hitPadding = 0.1f;

        [Header("Start Conditions")]
        [Tooltip("Max angle to the player (degrees) before a swing may start.")]
        [SerializeField] private float maxStartAngle = 40f;
        [Tooltip("Turn speed (deg/s) while swinging.")]
        [SerializeField] private float swingTurnSpeed = 90f;

        [Header("Animator")]
        [SerializeField] private string attackingBool = "Attacking";
        [SerializeField] private string attackSpeedFloat = "AttackSpeed";
        [SerializeField] private string attackIndexInt = "AttackIndex";
        [Tooltip("Layer faded to 0 while swinging so the arm overlay doesn't fight the attack. Empty = none.")]
        [SerializeField] private string armsLayerName = "Arms";
        [SerializeField] private float armsLayerFade = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool logHits = false;

        public bool IsSwinging { get; private set; }
        public bool IsCoolingDown => Time.time < cooldownUntil;
        public float Range => Stat(statKeys != null ? statKeys.attackRange : null, baseRange);
        private float Cooldown => Stat(statKeys != null ? statKeys.attackCooldown : null, baseCooldown);
        private float AttackSpeed => Mathf.Max(0.05f, Stat(statKeys != null ? statKeys.attackSpeed : null, baseAttackSpeed));

        private readonly Collider[] overlap = new Collider[8];
        private AttackMove current;
        private int lastMove = -1;
        private Transform player;
        private float swingTime;
        private float swingSpeed;
        private bool hitThisSwing;
        private float cooldownUntil;
        private int attackingHash;
        private int speedHash;
        private int indexHash;
        private int armsLayer = -1;
        private float armsWeight = 1f;

        // ---- IDamageSource ----
        public List<IHitEffect> GetEffects() => current != null ? current.Effects : null;
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

            attackingHash = Animator.StringToHash(attackingBool);
            speedHash = Animator.StringToHash(attackSpeedFloat);
            indexHash = Animator.StringToHash(attackIndexInt);

            BuildMoves();
        }

        private void Start()
        {
            PushBases();
            FindPlayer();
            if (animator != null && !string.IsNullOrEmpty(armsLayerName))
                armsLayer = animator.GetLayerIndex(armsLayerName);
        }

        private void OnDisable() => CancelSwing();

        public void OnSpawn()
        {
            CancelSwing();
            cooldownUntil = 0f;
            FindPlayer();
        }

        public void OnDespawn() => CancelSwing();

        // True when a swing could start this frame (range, angle, cooldown).
        public bool CanStartAttack()
        {
            if (IsSwinging || IsCoolingDown || player == null || moves.Count == 0) return false;
            if (combatantStats != null && combatantStats.IsDying) return false;

            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.magnitude > Range) return false;
            return Vector3.Angle(Flat(transform.forward), to) <= maxStartAngle;
        }

        public bool TryStartAttack()
        {
            if (!CanStartAttack()) return false;

            int index = PickMove();
            current = moves[index];
            lastMove = index;

            IsSwinging = true;
            hitThisSwing = false;
            swingTime = 0f;
            swingSpeed = AttackSpeed;

            if (animator != null)
            {
                animator.SetInteger(indexHash, index);
                animator.SetFloat(speedHash, swingSpeed);
                animator.SetBool(attackingHash, true);
            }
            return true;
        }

        public void CancelSwing()
        {
            if (!IsSwinging) return;
            IsSwinging = false;
            if (animator != null && animator.isActiveAndEnabled) animator.SetBool(attackingHash, false);
        }

        private void Update()
        {
            UpdateArmsLayer();
            if (!IsSwinging) return;

            if (combatantStats != null && combatantStats.IsDying) { CancelSwing(); return; }

            swingTime += Time.deltaTime;
            float frame = swingTime * current.clipFrameRate * swingSpeed;

            if (player != null && swingTurnSpeed > 0f)
            {
                Vector3 to = Flat(player.position - transform.position);
                if (to.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, Quaternion.LookRotation(to), swingTurnSpeed * Time.deltaTime);
            }

            if (frame >= current.recoveryEndFrame)
            {
                CancelSwing();
                cooldownUntil = Time.time + Cooldown;
            }
        }

        // Hit test after animation so the arm colliders are in this frame's pose.
        private void LateUpdate()
        {
            if (!IsSwinging || hitThisSwing) return;

            float frame = swingTime * current.clipFrameRate * swingSpeed;
            if (frame < current.hitStartFrame || frame > current.hitEndFrame) return;

            var cols = current.Colliders;
            for (int i = 0; i < cols.Count; i++)
            {
                var col = cols[i];
                if (col == null || !col.enabled) continue;

                int n = Overlap(col);
                for (int k = 0; k < n; k++)
                {
                    var hurtbox = overlap[k].GetComponentInParent<PlayerHurtbox>();
                    if (hurtbox == null) continue;

                    hitThisSwing = true;
                    DeliverHit(hurtbox, overlap[k].ClosestPoint(col.bounds.center));
                    return;
                }
            }
        }

        private void DeliverHit(PlayerHurtbox hurtbox, Vector3 point)
        {
            if (resolver == null) { Debug.LogWarning($"[EnemyMeleeAttack] '{name}' has no WeaponHitResolver."); return; }

            var ctx = new HitContext
            {
                Target = hurtbox.Target,
                HitPoint = point,
                SourceFaction = Faction,
                DamageType = damageType,
                Source = HitSource.Direct,
                BodyPartHit = hurtbox.BodyPart,
                ApplyStatusTickDamage = hurtbox.ApplyDamage,
                Attacker = combatantStats,
                Effects = current.Effects,
                DamageSource = this,
                DedupMode = HitDedupMode.PerShot,
                ShowFloatingNumber = false
            };

            hurtbox.Receive(resolver, ctx, transform);

            if (logHits)
                Debug.Log($"[EnemyMeleeAttack] {name} hit player for {ctx.DamageDealt:F1}");
        }

        private int Overlap(Collider col)
        {
            Transform t = col.transform;
            Vector3 s = t.lossyScale;
            s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

            if (col is CapsuleCollider cap)
            {
                Vector3 axis; float axisScale, radScale;
                switch (cap.direction)
                {
                    case 0: axis = t.right; axisScale = s.x; radScale = Mathf.Max(s.y, s.z); break;
                    case 2: axis = t.forward; axisScale = s.z; radScale = Mathf.Max(s.x, s.y); break;
                    default: axis = t.up; axisScale = s.y; radScale = Mathf.Max(s.x, s.z); break;
                }
                float r = cap.radius * radScale;
                float half = Mathf.Max(0f, cap.height * axisScale * 0.5f - r);
                Vector3 c = t.TransformPoint(cap.center);
                return Physics.OverlapCapsuleNonAlloc(c + axis * half, c - axis * half, r + hitPadding,
                    overlap, playerMask, QueryTriggerInteraction.Collide);
            }
            if (col is BoxCollider box)
            {
                Vector3 ext = Vector3.Scale(box.size, s) * 0.5f + Vector3.one * hitPadding;
                return Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center), ext, overlap, t.rotation,
                    playerMask, QueryTriggerInteraction.Collide);
            }
            if (col is SphereCollider sph)
            {
                float r = sph.radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                return Physics.OverlapSphereNonAlloc(t.TransformPoint(sph.center), r + hitPadding,
                    overlap, playerMask, QueryTriggerInteraction.Collide);
            }
            Bounds b = col.bounds;
            return Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * hitPadding, overlap,
                Quaternion.identity, playerMask, QueryTriggerInteraction.Collide);
        }

        // Per move: the colliders it hits with and its damage effect.
        private void BuildMoves()
        {
            var hitboxes = GetComponentsInChildren<EnemyHitbox>(true);
            var attackStat = statKeys != null ? statKeys.attackDamage : null;

            foreach (var m in moves)
            {
                m.Colliders = new List<Collider>();
                foreach (var hb in hitboxes)
                {
                    if (m.hitParts == null || Array.IndexOf(m.hitParts, hb.bodyPart) < 0) continue;
                    var col = hb.GetComponent<Collider>();
                    if (col != null) m.Colliders.Add(col);
                }
                if (m.Colliders.Count == 0)
                    Debug.LogWarning($"[EnemyMeleeAttack] '{name}' move '{m.label}' found no hitboxes for its hit parts.");

                m.Effects = new List<IHitEffect>(1);
                m.Effects.Add(attackStat != null
                    ? new DamageHitEffect(new DamageSpec(attackStat, StatScope.Source, m.damageMultiplier, damageType, false))
                    : new DamageHitEffect(new DamageSpec(baseDamage * m.damageMultiplier, damageType, false)));
            }
        }

        // Weighted random, skipping the last move when avoidRepeat is on.
        private int PickMove()
        {
            if (moves.Count == 1) return 0;

            float total = 0f;
            for (int i = 0; i < moves.Count; i++)
                if (!(avoidRepeat && i == lastMove)) total += moves[i].weight;
            if (total <= 0f) return (lastMove + 1) % moves.Count;

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < moves.Count; i++)
            {
                if (avoidRepeat && i == lastMove) continue;
                roll -= moves[i].weight;
                if (roll <= 0f) return i;
            }
            return moves.Count - 1;
        }

        private void UpdateArmsLayer()
        {
            if (armsLayer < 0 || animator == null) return;
            float target = IsSwinging ? 0f : 1f;
            float step = armsLayerFade > 0f ? Time.deltaTime / armsLayerFade : 1f;
            armsWeight = Mathf.MoveTowards(armsWeight, target, step);
            animator.SetLayerWeight(armsLayer, armsWeight);
        }

        private void PushBases()
        {
            var c = combatantStats != null ? combatantStats.Container : null;
            if (c == null || statKeys == null) return;
            if (statKeys.attackDamage != null) c.SetBase(statKeys.attackDamage, baseDamage);
            if (statKeys.attackRange != null) c.SetBase(statKeys.attackRange, baseRange);
            if (statKeys.attackCooldown != null) c.SetBase(statKeys.attackCooldown, baseCooldown);
            if (statKeys.attackSpeed != null) c.SetBase(statKeys.attackSpeed, baseAttackSpeed);
        }

        private void FindPlayer()
        {
            var go = GameObject.FindWithTag("Player");
            player = go != null ? go.transform : null;
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
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? Range : baseRange);
        }
#endif
    }
}
