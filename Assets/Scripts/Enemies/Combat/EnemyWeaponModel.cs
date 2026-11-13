using System.Collections.Generic;
using UnityEngine;
using Combat.AI;
using Combat.Core;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Enemies
{
    // Holds a weapon's world model by its TP grip, same as the player body: barrel onto the animated left hand for long guns, aimed through the spine.
    [DefaultExecutionOrder(120)]
    [RequireComponent(typeof(Animator))]
    public class EnemyWeaponModel : MonoBehaviour
    {
        [Tooltip("Visual only: gun prefab, skin and shader come from its viewmodel. Damage comes from the ranged stats.")]
        [SerializeField] private WeaponSO weapon;

        [Header("Aim")]
        [Tooltip("Seconds to blend the barrel onto the aim line when raising or lowering.")]
        [SerializeField, Min(0.01f)] private float aimBlend = 0.2f;
        [Tooltip("Share of the aim turn per bone: spine, chest, upper chest.")]
        [SerializeField] private Vector3 spineWeights = new Vector3(0.3f, 0.3f, 0.4f);
        [Tooltip("Height on the target the barrel points at between bursts (m).")]
        [SerializeField] private float targetAimHeight = 1.2f;

        [Header("Left hand")]
        [Tooltip("Seconds to blend the barrel onto the animated left hand.")]
        [SerializeField, Min(0.01f)] private float supportBlend = 0.15f;

        [Header("Fire kick")]
        [Tooltip("Upward upper-body kick per shot (degrees).")]
        [SerializeField] private float kickDegrees = 3f;
        [Tooltip("Seconds the kick takes to settle.")]
        [SerializeField, Min(0.01f)] private float kickTime = 0.12f;

        [Header("Reload")]
        [Tooltip("Gun roll during a reload (degrees). Flip the sign if it rolls away from the body.")]
        [SerializeField] private float reloadTilt = 25f;
        [Tooltip("Seconds to roll the gun in or out.")]
        [SerializeField, Min(0.01f)] private float tiltTime = 0.15f;

        public WeaponSO Weapon => weapon;
        public GunModel Gun { get; private set; }
        public Transform Muzzle => Gun != null ? Gun.Muzzle : null;

        private Animator animator;
        private EnemyRangedAttack ranged;
        private CombatantStats stats;
        private EnemyPerception perception;
        private readonly Transform[] spine = new Transform[3];
        private float aimWeight;
        private Transform rightHand, leftUpper, leftLower, leftHand, leftMiddle;
        private float kickClock = -1f, tilt;
        private Vector3 glueLocalPos;
        private Quaternion glueLocalRot = Quaternion.identity;
        private Vector3 baseLocalPos;
        private Quaternion baseLocalRot;
        private float supportWeight;
        private bool spawned;

        private void Awake() => EnsureModel();

        private void OnEnable()
        {
            if (ranged != null) ranged.Fired += HandleFired;
        }

        private void OnDisable()
        {
            if (ranged != null) ranged.Fired -= HandleFired;
            kickClock = -1f;
            tilt = 0f;
        }

        private void HandleFired() => kickClock = 0f;

        // Spawns and attaches the model once; safe to call from other components' Awake
        public GunModel EnsureModel()
        {
            if (spawned) return Gun;
            spawned = true;

            animator = GetComponent<Animator>();
            ranged = GetComponentInParent<EnemyRangedAttack>();
            stats = GetComponentInParent<CombatantStats>();
            perception = GetComponentInParent<EnemyPerception>();
            if (animator == null || !animator.isHuman) return null;

            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            leftMiddle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            spine[0] = animator.GetBoneTransform(HumanBodyBones.Spine);
            spine[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
            spine[2] = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (rightHand == null) return null;

            var model = ThirdPersonWeapon.SpawnWorldModel(weapon);
            if (model == null) return null;
            Gun = model.GetComponent<GunModel>();
            ThirdPersonWeapon.Attach(model.transform, Gun, rightHand, weapon.viewmodel.gunPrefab.transform.localScale);
            baseLocalPos = model.transform.localPosition;
            baseLocalRot = model.transform.localRotation;
            return Gun;
        }

        // After animation and look-at: gun back to its grip, barrel onto the animated left hand, then aimed through the spine
        private void LateUpdate()
        {
            if (Gun == null) return;
            if (stats != null && stats.IsDying) { supportWeight = aimWeight = 0f; return; }

            float dt = Time.deltaTime;
            supportWeight = Mathf.MoveTowards(supportWeight, animator.enabled ? 1f : 0f, dt / supportBlend);
            bool reloading = ranged != null && ranged.IsReloading;
            tilt = Mathf.MoveTowards(tilt, reloading ? 1f : 0f, dt / tiltTime);

            var root = Gun.transform;
            root.localPosition = baseLocalPos;
            root.localRotation = baseLocalRot;
            if (leftHand != null)
            {
                ThirdPersonWeapon.AlignToLeftHand(root, Gun, ThirdPersonWeapon.PalmOf(leftHand, leftMiddle), supportWeight);
                // Where the animation's left palm sits on the gun before the procedural moves
                glueLocalPos = root.InverseTransformPoint(ThirdPersonWeapon.PalmOf(leftHand, leftMiddle));
                glueLocalRot = Quaternion.Inverse(root.rotation) * leftHand.rotation;
            }

            UpdateAim();
            ApplyKick(dt);

            // Reload: roll the gun about its barrel; the left hand stays with the hold pose
            if (tilt > 0f && rightHand != null)
            {
                Vector3 barrel = root.TransformDirection(Gun.TpAimAxis);
                rightHand.rotation = Quaternion.AngleAxis(reloadTilt * Mathf.SmoothStep(0f, 1f, tilt), barrel) * rightHand.rotation;
            }

            // Left palm onto TP_Support (when the gun asks for it), else kept where the animation had it on the gun
            var support = Gun.TpLeftHandIK ? Gun.TpSupport : null;
            Vector3 palm = support != null ? support.position : root.TransformPoint(glueLocalPos);
            Quaternion palmRot = support != null ? support.rotation : root.rotation * glueLocalRot;
            ThirdPersonWeapon.PlaceLeftHand(leftUpper, leftLower, leftHand, leftMiddle, palm, palmRot, supportWeight);
        }

        // Upper body pitches up on each shot and settles back
        private void ApplyKick(float dt)
        {
            if (kickClock < 0f) return;
            kickClock += dt;
            if (kickClock >= kickTime) { kickClock = -1f; return; }

            float degrees = -kickDegrees * (1f - kickClock / kickTime);
            Vector3 axis = transform.right;
            float total = spineWeights.x + spineWeights.y + spineWeights.z;
            if (total <= 0f) return;
            for (int i = 0; i < spine.Length; i++)
                if (spine[i] != null) spine[i].rotation = Quaternion.AngleAxis(degrees * spineWeights[i] / total, axis) * spine[i].rotation;
        }

        // Raised weapon: turn the spine so the barrel points at the aim point (burst aim, else the believed target)
        private void UpdateAim()
        {
            var phase = ranged != null ? ranged.CurrentPhase : EnemyRangedAttack.Phase.Lowered;
            bool raised = animator.enabled && (phase == EnemyRangedAttack.Phase.Raising || phase == EnemyRangedAttack.Phase.Ready
                                               || phase == EnemyRangedAttack.Phase.Bursting);
            bool hasPoint = perception != null && (perception.HasTarget || perception.HasLastKnown);
            aimWeight = Mathf.MoveTowards(aimWeight, raised && hasPoint ? 1f : 0f, Time.deltaTime / aimBlend);
            if (aimWeight <= 0f || !hasPoint) return;

            Vector3 point = phase == EnemyRangedAttack.Phase.Bursting && ranged.LastAimPoint != Vector3.zero
                ? ranged.LastAimPoint
                : perception.BelievedPosition + Vector3.up * targetAimHeight;
            var gun = Gun.transform;
            Vector3 dir = point - gun.position;
            if (dir.sqrMagnitude < 0.01f) return;
            ThirdPersonWeapon.AimSpine(gun, Gun.TpAimAxis, spine, spineWeights, dir.normalized, aimWeight);
        }

        // Spawned gun's muzzle, or the prefab's before play
        public bool HasMuzzle
        {
            get
            {
                if (Gun != null) return Gun.Muzzle != null;
                var gun = PrefabGun();
                return gun != null && gun.Muzzle != null;
            }
        }

        private GunModel PrefabGun()
        {
            var prefab = weapon != null && weapon.viewmodel != null ? weapon.viewmodel.gunPrefab : null;
            return prefab != null ? prefab.GetComponent<GunModel>() : null;
        }

        // Problems the debug markers list
        public void CollectIssues(List<string> issues)
        {
            var anim = GetComponent<Animator>();
            if (anim == null || anim.avatar == null || !anim.avatar.isHuman) issues.Add("Weapon model: Animator is not Humanoid");
            if (weapon == null) { issues.Add("Weapon model: no weapon"); return; }
            var prefab = weapon.viewmodel != null ? weapon.viewmodel.gunPrefab : null;
            if (prefab == null) { issues.Add($"Weapon model: '{weapon.name}' has no viewmodel gun prefab"); return; }
            var gun = prefab.GetComponent<GunModel>();
            if (gun == null || gun.TpGrip == null) issues.Add($"Weapon model: '{prefab.name}' has no TP grip (Tools > TP Grip Tool)");
            if (gun != null && gun.Muzzle == null) issues.Add($"Weapon model: '{prefab.name}' has no muzzle");
        }
    }
}
