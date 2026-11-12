using System.Collections.Generic;
using UnityEngine;
using Combat.AI;
using Combat.Core;
using Combat.Stats;
using Combat.Weapons;

namespace Combat.Enemies
{
    // Holds a weapon's world model by its TP grip, same as the player body, and puts the left hand on its support point.
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
        [Tooltip("Seconds to blend the support hand on or off.")]
        [SerializeField, Min(0.01f)] private float supportBlend = 0.15f;
        [Tooltip("0 = keep the clip's wrist rotation, 1 = copy the support point's rotation.")]
        [SerializeField, Range(0f, 1f)] private float supportRotationWeight = 0f;
        [Tooltip("Let the reload clip drive the left hand while reloading.")]
        [SerializeField] private bool releaseOnReload = true;

        public WeaponSO Weapon => weapon;
        public GunModel Gun { get; private set; }
        public Transform Muzzle => Gun != null ? Gun.Muzzle : null;

        private Animator animator;
        private EnemyRangedAttack ranged;
        private CombatantStats stats;
        private EnemyPerception perception;
        private readonly Transform[] spine = new Transform[3];
        private float aimWeight;
        private Transform leftUpper, leftLower, leftHand;
        private float supportWeight;
        private bool spawned;

        private void Awake() => EnsureModel();

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

            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            spine[0] = animator.GetBoneTransform(HumanBodyBones.Spine);
            spine[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
            spine[2] = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (rightHand == null) return null;

            var model = ThirdPersonWeapon.SpawnWorldModel(weapon);
            if (model == null) return null;
            Gun = model.GetComponent<GunModel>();
            ThirdPersonWeapon.Attach(model.transform, Gun, rightHand, weapon.viewmodel.gunPrefab.transform.localScale);
            return Gun;
        }

        // After animation and look-at: barrel onto the aim line, then the left hand onto the gun
        private void LateUpdate()
        {
            if (Gun == null) return;
            if (stats != null && stats.IsDying) { supportWeight = aimWeight = 0f; return; }

            UpdateAim();
            if (Gun.TpSupport == null) return;

            bool want = animator.enabled && !(releaseOnReload && ranged != null && ranged.IsReloading);
            supportWeight = Mathf.MoveTowards(supportWeight, want ? 1f : 0f, Time.deltaTime / supportBlend);
            if (supportWeight <= 0f) return;

            var s = Gun.TpSupport;
            ThirdPersonWeapon.SolveTwoBone(leftUpper, leftLower, leftHand, s.position, s.rotation,
                                           supportWeight, supportWeight * supportRotationWeight);
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
            if (gun != null && gun.TpSupport == null) issues.Add($"Weapon model: '{prefab.name}' has no TP support (left hand stays on the clip)");
        }
    }
}
