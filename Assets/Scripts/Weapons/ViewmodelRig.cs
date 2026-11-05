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
        [Tooltip("Debug: a dot at true screen center while aiming, to line the sight up against.")]
        [SerializeField] private bool debugCenterDot = false;

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
        private WeaponViewmodelSO viewmodel;
        private Transform sightPoint;
        private Quaternion baseRotation;
        public int ArmsEmptyStateHash { get; private set; }
        public int GunRestStateHash { get; private set; }

        private void Awake()
        {
            ArmsEmptyStateHash = Animator.StringToHash(armsEmptyState);
            GunRestStateHash = Animator.StringToHash(gunRestState);
            if (armsAnimator != null)
                ArmsActionsLayer = armsAnimator.GetLayerIndex(armsActionsLayer);

            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
            if (playerAim == null) playerAim = GetComponentInParent<PlayerAim>();
        }

        private void LateUpdate()
        {
            float blend = playerAim != null ? playerAim.Blend : 0f;
            transform.localPosition = basePosition + (aimOffset + Nudge()) * blend;
            transform.localRotation = baseRotation;
            Tilt(blend);
        }

        // The viewmodel's ADS tilt, rotated around the front sight so the post stays put.
        private void Tilt(float blend)
        {
            if (viewmodel == null || viewmodel.adsTiltDeg == Vector2.zero || sightPoint == null || viewmodelCamera == null || blend <= 0f) return;
            var cam = viewmodelCamera.transform;
            Vector3 pivot = sightPoint.position;
            transform.RotateAround(pivot, cam.right, -viewmodel.adsTiltDeg.x * blend);
            transform.RotateAround(pivot, cam.up, viewmodel.adsTiltDeg.y * blend);
        }

        // The viewmodel's ADS nudge, read live so it can be tuned in play mode.
        private Vector3 Nudge()
        {
            if (viewmodel == null || viewmodel.adsNudgeMm == Vector2.zero || viewmodelCamera == null) return Vector3.zero;
            Vector3 world = viewmodelCamera.transform.TransformVector(new Vector3(viewmodel.adsNudgeMm.x, viewmodel.adsNudgeMm.y, 0f) * 0.001f);
            return transform.parent != null ? transform.parent.InverseTransformVector(world) : world;
        }

        private void OnGUI()
        {
            if (!debugCenterDot || playerAim == null || playerAim.Blend < 0.99f) return;
            float x = Screen.width * 0.5f, y = Screen.height * 0.5f;
            var old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(x - 1f, y - 1f, 2f, 2f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Called by WeaponLoadout BEFORE OnEquipStarted fires. Swapping the controller
        // and rebinding clears triggers, so the Equip trigger must come after this.
        public void SetWeapon(WeaponFireController weapon, RuntimeAnimatorController controller,
                              Transform sight)
        {
            Current = weapon;
            var source = weapon != null ? weapon.DamageSource : null;
            viewmodel = source != null && source.Weapon != null ? source.Weapon.viewmodel : null;
            sightPoint = sight;
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
            transform.localRotation = baseRotation;
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