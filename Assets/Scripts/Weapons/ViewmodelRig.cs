using System;
using UnityEngine;

namespace Combat.Weapons
{
    // The single shared first-person arms rig. Weapons don't own arms: on equip,
    // WeaponLoadout hands the rig the weapon's arms controller, and gun models are
    // spawned onto the hand socket.
    //
    // Also the one place the BASE clips are declared. A weapon's clip length for a
    // role is found through its override controller (aoc[baseClip]), so adding a
    // weapon needs no clip names or lengths anywhere.
    //
    // SIGHT ALIGNMENT: on each equip the rig samples the weapon's AimPose clip, finds
    // where the gun's Sight point lands relative to the viewmodel camera's center
    // line, and stores the offset that would put it dead center. While aiming, that
    // offset is applied scaled by PlayerAim.Blend. Measured ONCE from the static pose,
    // not corrected per frame — per-frame correction would pin the sights and erase
    // aim-walk bob, breathing and recoil kick.
    public class ViewmodelRig : MonoBehaviour
    {
        [Serializable]
        public class ClipRoles
        {
            public AnimationClip fire;
            public AnimationClip reload;
            public AnimationClip reloadStart;
            public AnimationClip reloadStep;
            public AnimationClip reloadEnd;
            public AnimationClip equip;
            public AnimationClip stow;
            [Tooltip("Arms only: the static aimed pose, used to measure sight alignment.")]
            public AnimationClip aimPose;
        }

        [Header("Rig")]
        [Tooltip("Animator on Arms_Armature (not the FBX root — clip paths start at root/).")]
        [SerializeField] private Animator armsAnimator;
        [Tooltip("hand_item_r")]
        [SerializeField] private Transform gunSocket;

        [Header("Base clips (the clips used in the BASE controllers)")]
        public ClipRoles armsBaseClips = new ClipRoles();
        public ClipRoles gunBaseClips = new ClipRoles();

        [Header("Sight alignment")]
        [Tooltip("The overlay camera that renders the viewmodel.")]
        [SerializeField] private Camera viewmodelCamera;
        [Tooltip("Found in parents if empty.")]
        [SerializeField] private PlayerAim playerAim;

        [Header("Interrupt cross-fade targets")]
        [SerializeField] private string armsActionsLayer = "Actions";
        [SerializeField] private string armsEmptyState = "Empty";
        [SerializeField] private string gunRestState = "BasePose";

        public Animator Arms => armsAnimator;
        public Transform GunSocket => gunSocket;

        // The weapon whose controller the arms are currently running. Only that
        // weapon's WeaponAnimator may drive the arms.
        public WeaponFireController Current { get; private set; }

        public int ArmsActionsLayer { get; private set; } = -1;

        private Vector3 basePosition;
        private Vector3 aimOffset;      // parent-space shift that centers the sight
        public int ArmsEmptyStateHash { get; private set; }
        public int GunRestStateHash { get; private set; }

        private void Awake()
        {
            ArmsEmptyStateHash = Animator.StringToHash(armsEmptyState);
            GunRestStateHash = Animator.StringToHash(gunRestState);
            if (armsAnimator != null)
                ArmsActionsLayer = armsAnimator.GetLayerIndex(armsActionsLayer);

            basePosition = transform.localPosition;
            if (playerAim == null) playerAim = GetComponentInParent<PlayerAim>();
        }

        private void LateUpdate()
        {
            float blend = playerAim != null ? playerAim.Blend : 0f;
            transform.localPosition = basePosition + aimOffset * blend;
        }

        // Called by WeaponLoadout BEFORE OnEquipStarted fires. Swapping the controller
        // and rebinding clears triggers, so the Equip trigger must come after this.
        public void SetWeapon(WeaponFireController weapon, RuntimeAnimatorController controller,
                              Transform sight)
        {
            Current = weapon;
            if (armsAnimator == null || controller == null) return;

            if (armsAnimator.runtimeAnimatorController != controller)
                armsAnimator.runtimeAnimatorController = controller;

            MeasureSightAlignment(controller, sight);

            // Also restores the pose the measurement sampled over.
            armsAnimator.Rebind();
            armsAnimator.Update(0f);

            if (ArmsActionsLayer < 0)
                ArmsActionsLayer = armsAnimator.GetLayerIndex(armsActionsLayer);
        }

        private void MeasureSightAlignment(RuntimeAnimatorController controller, Transform sight)
        {
            aimOffset = Vector3.zero;
            var baseClip = armsBaseClips.aimPose;
            if (sight == null || viewmodelCamera == null || baseClip == null) return;

            var clip = controller is AnimatorOverrideController aoc ? aoc[baseClip] : baseClip;
            if (clip == null) return;

            transform.localPosition = basePosition;
            clip.SampleAnimation(armsAnimator.gameObject, 0f);

            // Where the sight sits relative to the camera's center line; zero X/Y
            // there means dead center on screen, whatever the FOV.
            var cam = viewmodelCamera.transform;
            Vector3 local = cam.InverseTransformPoint(sight.position);
            Vector3 worldShift = cam.TransformVector(new Vector3(-local.x, -local.y, 0f));

            aimOffset = transform.parent != null
                ? transform.parent.InverseTransformVector(worldShift)
                : worldShift;
        }

        // Length of the clip this controller plays for a base clip's role.
        public static float LengthOf(RuntimeAnimatorController controller, AnimationClip baseClip)
        {
            if (controller == null || baseClip == null) return 0f;
            var clip = controller is AnimatorOverrideController aoc ? aoc[baseClip] : baseClip;
            return clip != null ? clip.length : 0f;
        }
    }
}