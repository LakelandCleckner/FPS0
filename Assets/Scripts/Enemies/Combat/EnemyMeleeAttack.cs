using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
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

            [Tooltip("Furthest the enemy steps forward during the swing (m, 0 = no lunge).")]
            public float lungeDistance = 1.5f;
            [Tooltip("The lunge stops this far from the player's centre (m).")]
            public float strikeDistance = 1f;
            public int lungeStartFrame = 0;
            [Tooltip("-1 = the first damage frame.")]
            public int lungeEndFrame = -1;
            [Tooltip("On: follows the player during the wind-up. Off: aims where they stood when the swing started.")]
            public bool lungeTracks = true;
            [Tooltip("Keep chasing during the wind-up at this fraction of chase speed (0 = plant for the whole swing). Feet plant from the first damage frame.")]
            [Range(0f, 1f)] public float windupMoveSpeed = 0.6f;

            [NonSerialized] public List<Collider> Colliders;
            [NonSerialized] public List<IHitEffect> Effects;
        }

        [Header("References")]
        [SerializeField] private CombatantStats combatantStats;
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private Animator animator;
        [SerializeField] private EnemyAttackStatKeys statKeys;
        [Tooltip("Moves the enemy during lunges. Found on this object if empty.")]
        [SerializeField] private NavMeshAgent navAgent;
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

        public enum HitMode { Reach, Physical }

        [Header("Hit Detection")]
        [Tooltip("Reach: at the damage frames the player is hit if within Reach Distance and Reach Angle in front. Physical: an attacking limb collider must touch a player hitbox.")]
        [SerializeField] private HitMode hitMode = HitMode.Reach;
        [Tooltip("Reach mode: hit distance from this enemy's centre to the player's centre (m).")]
        [SerializeField] private float reachDistance = 1.8f;
        [Tooltip("Reach mode: full cone in front of the enemy (degrees).")]
        [SerializeField] private float reachAngle = 120f;
        [SerializeField] private LayerMask playerMask = 1 << 8;
        [Tooltip("Extra radius added to each arm collider so fast swings don't skip the player.")]
        [SerializeField] private float hitPadding = 0.1f;

        [Header("Start Conditions")]
        [Tooltip("Max angle to the player (degrees) before a swing may start.")]
        [SerializeField] private float maxStartAngle = 40f;
        [Tooltip("Turn speed (deg/s) while swinging.")]
        [SerializeField] private float swingTurnSpeed = 360f;
        [Tooltip("Don't swing when another enemy's body blocker is between this enemy and the player.")]
        [SerializeField] private bool requireClearLine = true;
        [Tooltip("Width of the clear-line check (m).")]
        [SerializeField] private float clearLineRadius = 0.25f;
        [Tooltip("Height of the clear-line check above the root (m).")]
        [SerializeField] private float clearLineHeight = 1.1f;

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

        // Swinging but not yet at the first damage frame
        public bool InWindup => IsSwinging && current != null
                                && swingTime * current.clipFrameRate * swingSpeed < current.hitStartFrame;

        // Chase speed fraction allowed during the wind-up (0 = planted)
        public float WindupMoveSpeed => current != null ? current.windupMoveSpeed : 0f;
        public bool IsCoolingDown => Time.time < cooldownUntil;
        public float Range => Stat(statKeys != null ? statKeys.attackRange : null, baseRange);
        private float Cooldown => Stat(statKeys != null ? statKeys.attackCooldown : null, baseCooldown);
        private float AttackSpeed => Mathf.Max(0.05f, Stat(statKeys != null ? statKeys.attackSpeed : null, baseAttackSpeed));

        private readonly Collider[] overlap = new Collider[16];
        private readonly RaycastHit[] lineHits = new RaycastHit[8];
        private PlayerHitbox[] playerHitboxes;
        private int lineCheckFrame = -1;
        private bool lineBlocked;
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
        private Vector3 lungeTarget;
        private float lunged;
        private EnemyDefinitionApplier definition;
        private DamageTypeSO activeDamageType;
        private readonly List<IHitEffect> extraEffects = new List<IHitEffect>();

        // ---- IDamageSource ----
        public List<IHitEffect> GetEffects() => current != null ? current.Effects : null;
        public int Faction => combatantStats != null ? combatantStats.Faction : 0;
        public DamageTypeSO BaseDamageType => activeDamageType;
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
            if (navAgent == null) navAgent = GetComponent<NavMeshAgent>();
            definition = GetComponent<EnemyDefinitionApplier>();
            activeDamageType = damageType;

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
            if (Vector3.Angle(Flat(transform.forward), to) > maxStartAngle) return false;
            return !LineBlocked();
        }

        // Another enemy's body blocker between this enemy and the player (checked once per frame)
        public bool LineBlocked()
        {
            if (!requireClearLine || player == null) return false;
            if (lineCheckFrame == Time.frameCount) return lineBlocked;
            lineCheckFrame = Time.frameCount;
            lineBlocked = false;

            int mask = PhysicsLayers.BlockerMask;
            if (mask == 0) return false;

            Vector3 from = transform.position + Vector3.up * clearLineHeight;
            Vector3 to = player.position; to.y = from.y;
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.01f) return false;

            int n = Physics.SphereCastNonAlloc(from, clearLineRadius, d / dist, lineHits, dist,
                                               mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = lineHits[i].collider;
                if (col.transform.IsChildOf(transform)) continue;   // own blocker
                lineBlocked = true;
                break;
            }
            return lineBlocked;
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
            lungeTarget = player.position;
            lunged = 0f;

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

        // Stagger: cancels the wind-up (or any swing with cancelStrike) and holds off new swings
        public void Interrupt(bool cancelStrike, float lockout)
        {
            if (IsSwinging && (cancelStrike || InWindup)) CancelSwing();
            cooldownUntil = Mathf.Max(cooldownUntil, Time.time + lockout);
        }

        private void Update()
        {
            UpdateArmsLayer();
            if (!IsSwinging) return;

            if (combatantStats != null && combatantStats.IsDying) { CancelSwing(); return; }

            float prevFrame = swingTime * current.clipFrameRate * swingSpeed;
            swingTime += Time.deltaTime;
            float frame = swingTime * current.clipFrameRate * swingSpeed;
            Lunge(prevFrame, frame);

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

            if (hitMode == HitMode.Reach)
            {
                ReachHit();
                return;
            }

            var cols = current.Colliders;
            for (int i = 0; i < cols.Count; i++)
            {
                var col = cols[i];
                if (col == null || !col.enabled) continue;

                int n = Overlap(col);
                Vector3 from = col.bounds.center;
                if (col is MeshCollider) n = KeepTouching(col, n);

                // Closest player hitbox to the attacking limb; any player collider as fallback
                PlayerHitbox best = null;
                float bestDist = float.MaxValue;
                Collider fallback = null;
                for (int k = 0; k < n; k++)
                {
                    var hb = overlap[k].GetComponent<PlayerHitbox>();
                    if (hb != null && hb.Hurtbox != null)
                    {
                        float d = (overlap[k].ClosestPoint(from) - from).sqrMagnitude;
                        if (d < bestDist) { bestDist = d; best = hb; }
                    }
                    else if (fallback == null && overlap[k].GetComponentInParent<PlayerHurtbox>() != null)
                    {
                        fallback = overlap[k];
                    }
                }

                if (best != null)
                {
                    hitThisSwing = true;
                    DeliverHit(best.Hurtbox, best.GetComponent<Collider>().ClosestPoint(from),
                               best.bodyPart, best.damageMultiplier, best.ApplyTickDamage);
                    return;
                }
                if (fallback != null)
                {
                    var hurtbox = fallback.GetComponentInParent<PlayerHurtbox>();
                    hitThisSwing = true;
                    DeliverHit(hurtbox, fallback.ClosestPoint(from), hurtbox.BodyPart, 1f, hurtbox.ApplyDamage);
                    return;
                }
            }
        }

        // In reach and in front at the damage frames: hit the player's hitbox nearest the attacking limb
        private void ReachHit()
        {
            if (player == null) return;
            Vector3 to = Flat(player.position - transform.position);
            if (to.magnitude > reachDistance) return;
            if (Vector3.Angle(Flat(transform.forward), to) > reachAngle * 0.5f) return;
            if (LineBlocked()) return;

            Vector3 from = transform.position + Vector3.up * clearLineHeight;
            foreach (var col in current.Colliders)
                if (col != null && col.enabled) { from = col.bounds.center; break; }

            if (playerHitboxes == null || playerHitboxes.Length == 0)
                playerHitboxes = player.GetComponentsInChildren<PlayerHitbox>(true);

            PlayerHitbox best = null;
            Vector3 bestPoint = player.position;
            float bestDist = float.MaxValue;
            foreach (var hb in playerHitboxes)
            {
                if (hb == null || hb.Hurtbox == null) continue;
                var c = hb.GetComponent<Collider>();
                if (c == null || !c.enabled || !hb.gameObject.activeInHierarchy) continue;
                Vector3 p = c.ClosestPoint(from);
                float d = (p - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = hb; bestPoint = p; }
            }

            hitThisSwing = true;
            if (best != null)
            {
                DeliverHit(best.Hurtbox, bestPoint, best.bodyPart, best.damageMultiplier, best.ApplyTickDamage);
                return;
            }

            var hurtbox = player.GetComponentInParent<PlayerHurtbox>();
            if (hurtbox == null) hurtbox = player.GetComponentInChildren<PlayerHurtbox>();
            if (hurtbox != null) DeliverHit(hurtbox, player.position, hurtbox.BodyPart, 1f, hurtbox.ApplyDamage);
        }

        private void DeliverHit(PlayerHurtbox hurtbox, Vector3 point, BodyPart part, float multiplier,
                                Action<float, DamageTypeSO> tickDamage)
        {
            if (resolver == null) { Debug.LogWarning($"[EnemyMeleeAttack] '{name}' has no WeaponHitResolver."); return; }

            var ctx = new HitContext
            {
                Target = hurtbox.Target,
                HitPoint = point,
                SourceFaction = Faction,
                DamageType = activeDamageType,
                Source = HitSource.Direct,
                BodyPartHit = part,
                HitboxMultiplier = multiplier,
                ApplyStatusTickDamage = tickDamage,
                Attacker = combatantStats,
                Effects = current.Effects,
                DamageSource = this,
                DedupMode = HitDedupMode.PerShot,
                ShowFloatingNumber = false
            };

            hurtbox.Receive(resolver, ctx, transform);

            if (logHits)
                Debug.Log($"[EnemyMeleeAttack] {name} hit player {part} for {ctx.DamageDealt:F1}");
        }

        // Steps toward the player across the lunge frames, stopping at strike distance; stays on the navmesh
        private void Lunge(float prevFrame, float frame)
        {
            if (current.lungeDistance <= 0f || navAgent == null || player == null) return;
            if (!navAgent.isActiveAndEnabled || !navAgent.isOnNavMesh) return;

            float start = current.lungeStartFrame;
            float end = current.lungeEndFrame >= 0 ? current.lungeEndFrame : current.hitStartFrame;
            if (end <= start || frame <= start || prevFrame >= end) return;

            Vector3 to = Flat((current.lungeTracks ? player.position : lungeTarget) - transform.position);
            float remaining = Mathf.Min(to.magnitude - current.strikeDistance, current.lungeDistance - lunged);
            if (remaining <= 0f) return;

            // Share of what is left, by how much of the lunge window this frame covers
            float from = Mathf.Max(prevFrame, start);
            float share = Mathf.Clamp01((Mathf.Min(frame, end) - from) / (end - from));
            float step = remaining * share;
            navAgent.Move(to.normalized * step);
            lunged += step;
        }

        // Hull attack limbs: keep only colliders within hitPadding of the limb (the bounds box overlap is coarse)
        private int KeepTouching(Collider limb, int n)
        {
            int kept = 0;
            float pad2 = hitPadding * hitPadding;
            for (int k = 0; k < n; k++)
            {
                var other = overlap[k];
                bool touching;
                if (other is CharacterController)
                {
                    touching = true;   // ClosestPoint is unsupported there; the fallback path only
                }
                else
                {
                    Vector3 p = other.ClosestPoint(limb.bounds.center);
                    Vector3 q = limb.ClosestPoint(p);
                    touching = (p - q).sqrMagnitude <= pad2;
                }
                if (touching) overlap[kept++] = other;
            }
            return kept;
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

            foreach (var m in moves)
            {
                m.Colliders = new List<Collider>();
                foreach (var hb in hitboxes)
                {
                    if (m.hitParts == null || Array.IndexOf(m.hitParts, hb.bodyPart) < 0) continue;

                    // The hitbox's own colliders and its hull child; disabled ones are skipped at hit time
                    foreach (var col in hb.GetComponentsInChildren<Collider>(true))
                        if (col.GetComponentInParent<EnemyHitbox>() == hb) m.Colliders.Add(col);
                }
                if (m.Colliders.Count == 0)
                    Debug.LogWarning($"[EnemyMeleeAttack] '{name}' move '{m.label}' found no hitboxes for its hit parts.");
            }
            BuildEffects();
        }

        // Per move: its damage effect plus any extra on-hit effects from the enemy definition.
        private void BuildEffects()
        {
            var attackStat = statKeys != null ? statKeys.attackDamage : null;
            foreach (var m in moves)
            {
                m.Effects = new List<IHitEffect>(1 + extraEffects.Count);
                m.Effects.Add(attackStat != null
                    ? new DamageHitEffect(new DamageSpec(attackStat, StatScope.Source, m.damageMultiplier, activeDamageType, false))
                    : new DamageHitEffect(new DamageSpec(baseDamage * m.damageMultiplier, activeDamageType, false)));
                m.Effects.AddRange(extraEffects);
            }
        }

        // Damage type override (null = authored) and extra on-hit effects, set by the enemy definition.
        public void Configure(DamageTypeSO typeOverride, IReadOnlyList<IHitEffect> extras)
        {
            var type = typeOverride != null ? typeOverride : damageType;
            if (type == activeDamageType && SameEffects(extras)) return;   // pooled reuse, nothing changed

            activeDamageType = type;
            extraEffects.Clear();
            if (extras != null)
                for (int i = 0; i < extras.Count; i++)
                    if (extras[i] != null) extraEffects.Add(extras[i]);
            BuildEffects();
        }

        private bool SameEffects(IReadOnlyList<IHitEffect> extras)
        {
            int n = 0;
            if (extras != null)
                for (int i = 0; i < extras.Count; i++)
                {
                    if (extras[i] == null) continue;
                    if (n >= extraEffects.Count || extraEffects[n] != extras[i]) return false;
                    n++;
                }
            return n == extraEffects.Count;
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
            PushBase(c, statKeys.attackDamage, baseDamage);
            PushBase(c, statKeys.attackRange, baseRange);
            PushBase(c, statKeys.attackCooldown, baseCooldown);
            PushBase(c, statKeys.attackSpeed, baseAttackSpeed);
        }

        // Skips stats the enemy definition owns.
        private void PushBase(StatContainer c, StatDefinitionSO def, float value)
        {
            if (def == null) return;
            if (definition != null && definition.DefinesBase(def)) return;
            c.SetBase(def, value);
        }

        private void FindPlayer()
        {
            playerHitboxes = null;
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
