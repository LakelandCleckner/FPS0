using UnityEngine;
using Combat.AI;
using Combat.Core;
using Combat.Spawning;

namespace Combat.Enemies
{
    // Hit reactions: a procedural kick on every hit, a stagger meter, and hit clips on the HitReact layer.
    [DefaultExecutionOrder(130)]
    public class EnemyHitReaction : MonoBehaviour, IPoolable
    {
        public enum ClipRole { Stand, Walk, Run, Crouch, Heavy }

        // HitReact layer state per ClipRole
        public static readonly string[] StateNames = { "Hit_Stand", "Hit_Walk", "Hit_Run", "Hit_Crouch", "Hit_Heavy" };
        public const string SpeedParam = "HitReactSpeed";

        [Tooltip("Used when the enemy's archetype sets no hit reaction profile.")]
        [SerializeField] private HitReactionProfileSO defaultProfile;
        [Tooltip("Found in children if empty.")]
        [SerializeField] private Animator animator;
        [SerializeField] private string layerName = "HitReact";

        [Header("Debug")]
        [SerializeField] private bool logStaggers = false;

        public HitReactionProfileSO Profile { get; private set; }
        public bool Staggered => Time.time < staggerUntil;
        public bool HeavyStaggered => Staggered && heavy;
        // Share of max health x stagger power since the meter last emptied
        public float Meter => meter;
        public float Threshold => tuning.threshold;

        // true = heavy
        public event System.Action<bool> StaggerStarted;

        private static readonly float[] TorsoWeights = { 0.35f, 0.35f, 0.3f };
        private static readonly float[] HeadWeights = { 0.4f, 0.6f };

        private CombatantHealth health;
        private EnemyDefinitionApplier definition;
        private EnemyMover mover;
        private EnemyRangedAttack ranged;
        private EnemyMeleeAttack melee;
        private EnemyShooterAnimator shooterAnimator;
        private StaggerTuning tuning;

        private int layer = -1;
        private readonly int[] stateHashes = new int[StateNames.Length];
        private readonly int[] shortHashes = new int[StateNames.Length];
        private readonly bool[] hasState = new bool[StateNames.Length];
        private int speedHash;
        private bool hasSpeed;

        private float meter, lastHitTime, staggerUntil, immuneUntil, playStart;
        private bool heavy, playing;
        private int playingHash;
        private float layerWeight, layerTarget;

        private Transform hips;
        private readonly Transform[] torso = new Transform[3];
        private readonly Transform[] head = new Transform[2];
        private Vector3 torsoKick, torsoVel, headKick, headVel;

        private void Awake()
        {
            health = GetComponent<CombatantHealth>();
            definition = GetComponent<EnemyDefinitionApplier>();
            mover = GetComponent<EnemyMover>();
            ranged = GetComponent<EnemyRangedAttack>();
            melee = GetComponent<EnemyMeleeAttack>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) shooterAnimator = animator.GetComponent<EnemyShooterAnimator>();
            CacheBones();
        }

        private void OnEnable()
        {
            if (health != null) health.Damaged += HandleHit;
            if (definition != null) definition.Applied += Bind;
        }

        private void OnDisable()
        {
            if (health != null) health.Damaged -= HandleHit;
            if (definition != null) definition.Applied -= Bind;
            ResetState();
        }

        private void Start()
        {
            CacheLayer();
            Bind();
        }

        public void OnSpawn() => ResetState();
        public void OnDespawn() => ResetState();

        private void Bind()
        {
            var arch = definition != null ? definition.Archetype : null;
            Profile = arch != null && arch.hitReactionProfile != null ? arch.hitReactionProfile : defaultProfile;
            tuning = Profile != null ? Profile.For(definition != null ? definition.Rank : EnemyRank.Minor) : default;
        }

        private void ResetState()
        {
            meter = 0f;
            staggerUntil = immuneUntil = 0f;
            heavy = playing = false;
            layerWeight = layerTarget = 0f;
            torsoKick = torsoVel = headKick = headVel = Vector3.zero;
            if (animator != null && layer >= 0 && animator.isActiveAndEnabled) animator.SetLayerWeight(layer, 0f);
        }

        // ---- hits ----

        private void HandleHit(HitReport r)
        {
            var p = Profile;
            if (p == null || health.IsDying || !p.Accepts(r.Source)) return;
            float max = health.MaxHealth;
            if (max <= 0f) return;

            Kick(r, Mathf.Max(0f, r.Damage) / max, p);
            if (mover != null && p.flinchStopTime > 0f) mover.Stun(Time.time + p.flinchStopTime * Mathf.Max(0f, tuning.timeScale));
            if (ranged != null && p.aimJolt > 0f) ranged.Jolt(p.aimJolt * tuning.kickScale, p.aimJoltMax, p.aimJoltSettle);

            if (!tuning.canStagger || tuning.threshold <= 0f) return;
            float now = Time.time;
            float dealt = p.shieldDamageStaggers ? r.Damage : r.HealthDamage;
            float overload = tuning.threshold * Mathf.Max(1f, tuning.overloadMultiple);
            meter = Mathf.Min(meter + Mathf.Max(0f, dealt) / max * Mathf.Max(0f, r.StaggerPower), overload);
            lastHitTime = now;

            if (tuning.canHeavyStagger && meter >= overload && !HeavyStaggered) Begin(true, now);
            else if (meter >= tuning.threshold && !Staggered && now >= immuneUntil) Begin(false, now);
        }

        private void Begin(bool isHeavy, float now)
        {
            var p = Profile;
            var role = PickRole(isHeavy);
            meter = 0f;
            heavy = isHeavy;
            float time = (isHeavy ? p.heavyStaggerTime : p.staggerTime) * Mathf.Max(0f, tuning.timeScale);
            staggerUntil = now + time;
            immuneUntil = staggerUntil + p.immunityTime;

            if (mover != null) mover.Stun(staggerUntil);
            if (ranged != null) ranged.Interrupt(time, time + p.fireRecovery, isHeavy && p.heavyCancelsReload);
            if (melee != null) melee.Interrupt(isHeavy || p.staggerCancelsStrike, time);
            Play(role, p);

            StaggerStarted?.Invoke(isHeavy);
            if (logStaggers) Debug.Log($"[EnemyHitReaction] {name} {(isHeavy ? "heavy " : "")}stagger ({role}) for {time:F2}s", this);
        }

        // Clip by what the enemy was doing; missing states fall back to Stand
        private ClipRole PickRole(bool isHeavy)
        {
            var role = ClipRole.Stand;
            if (isHeavy) role = ClipRole.Heavy;
            else if (shooterAnimator != null && shooterAnimator.Crouched) role = ClipRole.Crouch;
            else if (mover != null)
            {
                Vector3 v = mover.Velocity;
                v.y = 0f;
                float speed = v.magnitude;
                if (speed >= Profile.runClipSpeed) role = ClipRole.Run;
                else if (speed >= Profile.walkClipSpeed) role = ClipRole.Walk;
            }
            return hasState[(int)role] ? role : ClipRole.Stand;
        }

        private void Play(ClipRole role, HitReactionProfileSO p)
        {
            if (animator == null || layer < 0 || !hasState[(int)role] || !animator.isActiveAndEnabled) return;
            if (hasSpeed) animator.SetFloat(speedHash, p.clipSpeed);
            playingHash = shortHashes[(int)role];
            // From rest the layer weight does the blend; mid-reaction cross-fade between clips
            if (layerWeight < 0.01f) animator.Play(stateHashes[(int)role], layer, 0f);
            else animator.CrossFadeInFixedTime(stateHashes[(int)role], p.blendIn, layer, 0f);
            playing = true;
            playStart = Time.time;
            layerTarget = 1f;
        }

        // ---- frame ----

        private void Update()
        {
            var p = Profile;
            if (p == null || meter <= 0f) return;
            if (Time.time - lastHitTime > p.drainDelay)
                meter = Mathf.MoveTowards(meter, 0f, p.drainRate * tuning.threshold * Time.deltaTime);
        }

        private void LateUpdate()
        {
            var p = Profile;
            if (p == null) return;
            float dt = Time.deltaTime;
            bool dying = health != null && health.IsDying;

            UpdateLayer(p, dt, dying);
            // No moving while a hit clip shows
            if (!dying && mover != null && (playing || layerWeight > 0.1f)) mover.Stun(Time.time + dt + 0.05f);
            if (dying) { torsoKick = torsoVel = headKick = headVel = Vector3.zero; return; }
            Step(ref torsoKick, ref torsoVel, p, dt);
            Step(ref headKick, ref headVel, p, dt);
            Rotate(torso, TorsoWeights, torsoKick);
            Rotate(head, HeadWeights, headKick);
        }

        // Layer in while the clip plays, out over its last blend-out seconds
        private void UpdateLayer(HitReactionProfileSO p, float dt, bool dying)
        {
            if (animator == null || layer < 0) return;
            if (dying) { playing = false; layerTarget = 0f; }
            if (playing)
            {
                var info = animator.GetCurrentAnimatorStateInfo(layer);
                if (info.shortNameHash == playingHash)
                {
                    if ((1f - info.normalizedTime) * info.length <= p.blendOut) { playing = false; layerTarget = 0f; }
                }
                else if (!animator.IsInTransition(layer) && Time.time - playStart > 0.25f)
                {
                    playing = false;
                    layerTarget = 0f;
                }
            }
            float blend = layerTarget > layerWeight ? p.blendIn : p.blendOut;
            float next = Mathf.MoveTowards(layerWeight, layerTarget, dt / Mathf.Max(0.01f, blend));
            if (Mathf.Approximately(next, layerWeight) && next == 0f) return;
            layerWeight = next;
            if (animator.isActiveAndEnabled) animator.SetLayerWeight(layer, layerWeight);
        }

        // ---- kick ----

        // Torque from the hit about the hips: bends away from the shot and twists on off-centre hits
        private void Kick(in HitReport r, float share, HitReactionProfileSO p)
        {
            if (hips == null || r.Direction.sqrMagnitude < 0.0001f) return;
            Vector3 axis = Vector3.Cross(r.Point - hips.position, r.Direction);
            if (axis.sqrMagnitude < 0.000001f) axis = Vector3.Cross(Vector3.up, r.Direction);
            if (axis.sqrMagnitude < 0.000001f) return;
            axis.Normalize();

            float peak = Mathf.Min(p.kickBase + share * p.kickPerHealth, p.kickMax) * tuning.kickScale;
            if (r.Part == BodyPart.Arm || r.Part == BodyPart.Leg) peak *= p.limbKickScale;
            // Start speed that peaks a critically damped spring at `peak`
            float v0 = peak * Mathf.Sqrt(Mathf.Max(1f, p.kickStiffness)) * 2.718f;
            bool headHit = r.Part == BodyPart.Head;
            torsoVel += axis * (v0 * (headHit ? 0.4f : 1f));
            headVel += axis * (v0 * (headHit ? 1f : 0.3f));
        }

        private static void Step(ref Vector3 x, ref Vector3 v, HitReactionProfileSO p, float dt)
        {
            if (x == Vector3.zero && v == Vector3.zero) return;
            v += (-p.kickStiffness * x - p.kickDamping * v) * dt;
            x += v * dt;
            x = Vector3.ClampMagnitude(x, p.kickMax * 1.5f);
            if (x.sqrMagnitude < 0.0001f && v.sqrMagnitude < 0.01f) x = v = Vector3.zero;
        }

        private static void Rotate(Transform[] bones, float[] weights, Vector3 kick)
        {
            float deg = kick.magnitude;
            if (deg < 0.01f) return;
            Vector3 axis = kick / deg;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].rotation = Quaternion.AngleAxis(deg * weights[i], axis) * bones[i].rotation;
        }

        // ---- setup ----

        private void CacheBones()
        {
            if (animator == null) return;
            if (animator.isHuman)
            {
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                torso[0] = animator.GetBoneTransform(HumanBodyBones.Spine);
                torso[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
                torso[2] = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                head[0] = animator.GetBoneTransform(HumanBodyBones.Neck);
                head[1] = animator.GetBoneTransform(HumanBodyBones.Head);
                return;
            }
            // Generic Mixamo rig: by name
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int c = n.LastIndexOf(':');
                if (c >= 0) n = n.Substring(c + 1);
                switch (n)
                {
                    case "Hips": hips = t; break;
                    case "Spine": torso[0] = t; break;
                    case "Spine1": torso[1] = t; break;
                    case "Spine2": torso[2] = t; break;
                    case "Neck": head[0] = t; break;
                    case "Head": head[1] = t; break;
                }
            }
        }

        private void CacheLayer()
        {
            layer = -1;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            layer = animator.GetLayerIndex(layerName);
            if (layer < 0) return;
            for (int i = 0; i < StateNames.Length; i++)
            {
                shortHashes[i] = Animator.StringToHash(StateNames[i]);
                stateHashes[i] = Animator.StringToHash(layerName + "." + StateNames[i]);
                hasState[i] = animator.HasState(layer, stateHashes[i]);
            }
            speedHash = Animator.StringToHash(SpeedParam);
            foreach (var prm in animator.parameters)
                if (prm.nameHash == speedHash) hasSpeed = true;
            animator.SetLayerWeight(layer, 0f);
        }
    }
}
