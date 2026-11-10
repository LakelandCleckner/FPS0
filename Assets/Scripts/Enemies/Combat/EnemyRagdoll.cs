using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using Combat.AI;
using Combat.Core;
using Combat.Spawning;

namespace Combat.Enemies
{
    // Ragdoll death: AI and animation switch off, hit hulls swap for the bone colliders, the bones fall.
    // Corpses stay until the lifetime or the cap recycles them (sink, then back to the pool).
    [RequireComponent(typeof(CombatantHealth))]
    public class EnemyRagdoll : MonoBehaviour, IPoolable
    {
        [Header("Corpses")]
        [Tooltip("Seconds a corpse stays before sinking (0 = until the cap recycles it).")]
        [SerializeField] private float lifetime = 0f;
        [Tooltip("Most corpses at once; past this the oldest sinks and returns to the pool.")]
        [SerializeField] private int maxCorpses = 50;
        [Tooltip("Seconds to sink through the floor.")]
        [SerializeField] private float sinkTime = 1f;
        [Tooltip("How far the body sinks (m).")]
        [SerializeField] private float sinkDepth = 1.5f;

        [Header("Pushes (velocity change, m/s)")]
        [Tooltip("Killing shot.")]
        [SerializeField] private float killPush = 6f;
        [Tooltip("Shot into a corpse.")]
        [SerializeField] private float corpseShotPush = 2.5f;
        [Tooltip("On-kill explosion, at its centre.")]
        [SerializeField] private float explosionPush = 9f;
        [Tooltip("Upward bias of an explosion push.")]
        [SerializeField] private float explosionLift = 0.5f;
        [Tooltip("Share of a shot push the rest of the body gets (the struck bone gets all of it).")]
        [Range(0f, 1f)] [SerializeField] private float bodyShare = 0.3f;
        [Tooltip("Share of the enemy's movement speed the body keeps on death.")]
        [Range(0f, 1f)] [SerializeField] private float keepVelocity = 1f;

        [Header("Limbs")]
        [Tooltip("Joint angle limits are widened by this on death (1 = as authored).")]
        [Range(1f, 3f)] [SerializeField] private float jointLimitScale = 1.5f;
        [Tooltip("Angular damping on corpse bones; lower = floppier limbs.")]
        [SerializeField] private float limbAngularDamping = 0.05f;

        [Header("Colliders")]
        [Tooltip("Corpses keep the mesh-fit HB_Hull colliders instead of the simple bone colliders (test: may jitter where hulls overlap).")]
        [SerializeField] private bool hullCorpses = false;

        [Header("Extra components to switch off on death")]
        [SerializeField] private Behaviour[] extraDisable;

        public bool IsDead { get; private set; }

        private const float BlastMemory = 0.2f;

        private static readonly List<EnemyRagdoll> corpses = new List<EnemyRagdoll>();
        private static readonly List<(Vector3 centre, float radius, float time)> blasts
            = new List<(Vector3, float, float)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            corpses.Clear();
            blasts.Clear();
        }

        private struct ColliderState { public Collider col; public bool enabled; public bool trigger; }
        private struct BoneState { public Transform t; public Vector3 pos; public Quaternion rot; }

        private CombatantHealth health;
        private EnemyPoolAdapter adapter;
        private NavMeshAgent nav;
        private Animator animator;
        private readonly List<Behaviour> disableOnDeath = new List<Behaviour>();
        private readonly List<bool> wasEnabled = new List<bool>();

        private Rigidbody[] bodies;
        private bool[] bodyKinematic;
        private RigidbodyInterpolation[] bodyInterp;
        private CollisionDetectionMode[] bodyDetection;
        private ColliderState[] ragdollColliders;
        private ColliderState[] hitColliders;
        private bool usingHulls;
        private BoneState[] bones;
        private struct JointState { public CharacterJoint j; public SoftJointLimit low, high, s1, s2; }
        private JointState[] joints;
        private float[] bodyAngularDamping;
        private Transform hips;
        private Coroutine lifeRoutine;
        private bool sinking;

        private void Awake()
        {
            health = GetComponent<CombatantHealth>();
            adapter = GetComponent<EnemyPoolAdapter>();
            nav = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>(true);

            // Order matters: AI first, then movement, then the agent and animation
            Add(GetComponent<EnemyBrain>());
            Add(GetComponent<EnemyPerception>());
            Add(GetComponent<GoapActionProvider>());
            Add(GetComponent<AgentBehaviour>());
            Add(GetComponent<EnemyMover>());
            Add(GetComponent<EnemyMeleeAttack>());
            if (extraDisable != null) foreach (var b in extraDisable) Add(b);
            Add(nav);
            Add(animator);

            CacheRig();
        }

        private void Add(Behaviour b)
        {
            if (b != null && b != this && !disableOnDeath.Contains(b)) disableOnDeath.Add(b);
        }

        // Bone bodies (not the root's), their own colliders, and every other collider (hulls, blocker)
        private void CacheRig()
        {
            var rbList = new List<Rigidbody>();
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
                if (rb.gameObject != gameObject) rbList.Add(rb);
            bodies = rbList.ToArray();
            bodyKinematic = new bool[bodies.Length];
            bodyInterp = new RigidbodyInterpolation[bodies.Length];
            bodyDetection = new CollisionDetectionMode[bodies.Length];

            var ragdoll = new List<ColliderState>();
            var others = new List<ColliderState>();
            var boneList = new List<BoneState>();
            foreach (var rb in bodies)
            {
                boneList.Add(new BoneState { t = rb.transform, pos = rb.transform.localPosition, rot = rb.transform.localRotation });
                if (hips == null && rb.GetComponent<Joint>() == null) hips = rb.transform;
            }
            bones = boneList.ToArray();

            var jointList = new List<JointState>();
            foreach (var j in GetComponentsInChildren<CharacterJoint>(true))
                jointList.Add(new JointState { j = j, low = j.lowTwistLimit, high = j.highTwistLimit, s1 = j.swing1Limit, s2 = j.swing2Limit });
            joints = jointList.ToArray();
            bodyAngularDamping = new float[bodies.Length];

            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                var s = new ColliderState { col = c, enabled = c.enabled, trigger = c.isTrigger };
                // Any collider under a bone body except the hit hulls (attachedRigidbody is null while disabled)
                var body = c.GetComponentInParent<Rigidbody>(true);
                bool isRagdoll = body != null && System.Array.IndexOf(bodies, body) >= 0 && !IsHull(c.transform);
                if (isRagdoll) ragdoll.Add(s); else others.Add(s);
            }
            ragdollColliders = ragdoll.ToArray();
            hitColliders = others.ToArray();

            if (bodies.Length == 0)
                Debug.LogWarning($"[EnemyRagdoll] {name}: no bone Rigidbodies found; corpse will just stand.", this);

            foreach (var rb in bodies)
            {
                bool has = false;
                foreach (var s in ragdollColliders)
                    if (s.col.GetComponentInParent<Rigidbody>(true) == rb) { has = true; break; }
                if (!has)
                    Debug.LogWarning($"[EnemyRagdoll] {name}: bone '{rb.name}' has no ragdoll collider; shots pass through it.", rb);
            }
        }

        private bool IsHull(Transform t)
        {
            for (; t != null && t != transform; t = t.parent)
                if (t.name.StartsWith("HB_Hull")) return true;
            return false;
        }

        private void OnEnable()
        {
            if (health != null) health.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDeath -= HandleDeath;
            corpses.Remove(this);
        }

        // ===== Death =====

        private void HandleDeath()
        {
            if (IsDead) return;
            IsDead = true;

            Vector3 inherited = nav != null && nav.enabled ? nav.velocity * keepVelocity : Vector3.zero;

            wasEnabled.Clear();
            foreach (var b in disableOnDeath)
            {
                wasEnabled.Add(b.enabled);
                b.enabled = false;
            }

            // Read at death so it can be flipped in Play mode
            usingHulls = hullCorpses;
            foreach (var s in hitColliders)
                s.col.enabled = usingHulls && s.enabled && IsHull(s.col.transform);
            if (!usingHulls)
                foreach (var s in ragdollColliders) { s.col.isTrigger = false; s.col.enabled = true; }

            for (int i = 0; i < bodies.Length; i++)
            {
                var rb = bodies[i];
                bodyKinematic[i] = rb.isKinematic;
                bodyInterp[i] = rb.interpolation;
                bodyDetection[i] = rb.collisionDetectionMode;
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // Stops small fast parts (the head) tunnelling into the floor
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.linearVelocity = inherited;
                rb.angularVelocity = Vector3.zero;
                bodyAngularDamping[i] = rb.angularDamping;
                rb.angularDamping = limbAngularDamping;
            }

            // Looser joints so limbs swing further
            foreach (var js in joints)
            {
                js.j.lowTwistLimit = Scaled(js.low);
                js.j.highTwistLimit = Scaled(js.high);
                js.j.swing1Limit = Scaled(js.s1);
                js.j.swing2Limit = Scaled(js.s2);
            }

            // Thrown by any on-kill blast that just went off (the blast's own kills resolve after it)
            float now = Time.time;
            blasts.RemoveAll(b => now - b.time > BlastMemory);
            foreach (var b in blasts)
                if ((transform.position - b.centre).sqrMagnitude <= b.radius * b.radius) ApplyBlast(b.centre, b.radius);

            corpses.Add(this);
            while (corpses.Count > Mathf.Max(1, maxCorpses)) corpses[0].BeginSink();

            if (lifetime > 0f) lifeRoutine = StartCoroutine(Lifetime());
        }

        private IEnumerator Lifetime()
        {
            yield return new WaitForSeconds(lifetime);
            lifeRoutine = null;
            BeginSink();
        }

        private void BeginSink()
        {
            corpses.Remove(this);
            if (sinking || !isActiveAndEnabled) return;
            sinking = true;
            if (lifeRoutine != null) { StopCoroutine(lifeRoutine); lifeRoutine = null; }
            StartCoroutine(Sink());
        }

        // Freeze the body, slide it down through the floor, then go home
        private IEnumerator Sink()
        {
            foreach (var rb in bodies) rb.isKinematic = true;
            foreach (var s in ragdollColliders) s.col.enabled = false;
            if (usingHulls) foreach (var s in hitColliders) s.col.enabled = false;

            if (hips != null && sinkTime > 0f)
            {
                Vector3 start = hips.position;
                for (float t = 0f; t < sinkTime; t += Time.deltaTime)
                {
                    hips.position = start + Vector3.down * (sinkDepth * t / sinkTime);
                    yield return null;
                }
            }

            if (adapter != null) adapter.Release();
            else Destroy(gameObject);
        }

        // ===== Pushes =====

        // Called by the resolver for a direct hit on a dead enemy: the killing shot, or a shot into a corpse
        public static void PushFromHit(HitContext ctx, bool killingShot)
        {
            if (ctx == null || ctx.Source != HitSource.Direct) return;
            var target = ctx.Target as Component;
            if (target == null) return;
            var ragdoll = target.GetComponent<EnemyRagdoll>();
            if (ragdoll == null || !ragdoll.IsDead || ragdoll.sinking) return;

            Vector3 from;
            if (ctx.Shot.HasOrigin) from = ctx.Shot.Origin;
            else if (ctx.Attacker is Component a) from = a.transform.position;
            else return;

            Vector3 dir = ctx.HitPoint - from;
            if (dir.sqrMagnitude < 0.0001f) return;

            float amount = (killingShot ? ragdoll.killPush : ragdoll.corpseShotPush) * ctx.Shot.DamageScale;
            ragdoll.Push(ctx.HitPoint, dir.normalized * amount);
        }

        // Pushes every corpse in the radius, and remembers the blast for enemies it kills a moment later
        public static void Blast(Vector3 centre, float radius)
        {
            if (radius <= 0f) return;
            blasts.Add((centre, radius, Time.time));

            float r2 = radius * radius;
            for (int i = corpses.Count - 1; i >= 0; i--)
            {
                var c = corpses[i];
                if (c == null || c.sinking) continue;
                if ((c.transform.position - centre).sqrMagnitude <= r2) c.ApplyBlast(centre, radius);
            }
        }

        private void Push(Vector3 point, Vector3 velocityChange)
        {
            Rigidbody struck = null;
            float best = float.MaxValue;
            foreach (var rb in bodies)
            {
                float d = (rb.worldCenterOfMass - point).sqrMagnitude;
                if (d < best) { best = d; struck = rb; }
            }

            // Struck bone is pushed at the hit point, so the limb spins about its joint
            foreach (var rb in bodies)
            {
                if (rb == struck) rb.AddForceAtPosition(velocityChange, point, ForceMode.VelocityChange);
                else rb.AddForce(velocityChange * bodyShare, ForceMode.VelocityChange);
            }
        }

        private SoftJointLimit Scaled(SoftJointLimit l)
        {
            l.limit = Mathf.Clamp(l.limit * jointLimitScale, -177f, 177f);
            return l;
        }

        private void ApplyBlast(Vector3 centre, float radius)
        {
            foreach (var rb in bodies)
                rb.AddExplosionForce(explosionPush, centre, radius * 1.2f, explosionLift, ForceMode.VelocityChange);
        }

        // ===== Pool =====

        // Restored before the pool deactivates, so every other reset sees a normal enemy
        public void OnDespawn()
        {
            if (!IsDead) return;
            StopAllCoroutines();
            lifeRoutine = null;
            corpses.Remove(this);

            for (int i = 0; i < bodies.Length; i++)
            {
                var rb = bodies[i];
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = bodyKinematic[i];
                rb.interpolation = bodyInterp[i];
                rb.collisionDetectionMode = bodyDetection[i];
                rb.angularDamping = bodyAngularDamping[i];
            }

            foreach (var js in joints)
            {
                js.j.lowTwistLimit = js.low;
                js.j.highTwistLimit = js.high;
                js.j.swing1Limit = js.s1;
                js.j.swing2Limit = js.s2;
            }

            foreach (var b in bones) b.t.SetLocalPositionAndRotation(b.pos, b.rot);
            foreach (var s in ragdollColliders) { s.col.enabled = s.enabled; s.col.isTrigger = s.trigger; }
            foreach (var s in hitColliders) s.col.enabled = s.enabled;

            for (int i = 0; i < disableOnDeath.Count && i < wasEnabled.Count; i++)
                disableOnDeath[i].enabled = wasEnabled[i];

            if (animator != null && animator.enabled)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            IsDead = false;
            sinking = false;
        }

        public void OnSpawn()
        {
            IsDead = false;
            sinking = false;
        }
    }
}
