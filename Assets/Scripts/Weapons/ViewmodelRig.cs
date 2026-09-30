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
        }

        [Header("Rig")]
        [Tooltip("Animator on Arms_Armature (not the FBX root — clip paths start at root/).")]
        [SerializeField] private Animator armsAnimator;
        [Tooltip("hand_item_r")]
        [SerializeField] private Transform gunSocket;

        [Header("Base clips (the clips used in the BASE controllers)")]
        public ClipRoles armsBaseClips = new ClipRoles();
        public ClipRoles gunBaseClips = new ClipRoles();

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
        public int ArmsEmptyStateHash { get; private set; }
        public int GunRestStateHash { get; private set; }

        private void Awake()
        {
            ArmsEmptyStateHash = Animator.StringToHash(armsEmptyState);
            GunRestStateHash = Animator.StringToHash(gunRestState);
            if (armsAnimator != null)
                ArmsActionsLayer = armsAnimator.GetLayerIndex(armsActionsLayer);
        }

        // Called by WeaponLoadout BEFORE OnEquipStarted fires. Swapping the controller
        // and rebinding clears triggers, so the Equip trigger must come after this.
        public void SetWeapon(WeaponFireController weapon, RuntimeAnimatorController controller)
        {
            Current = weapon;
            if (armsAnimator == null || controller == null) return;

            if (armsAnimator.runtimeAnimatorController != controller)
                armsAnimator.runtimeAnimatorController = controller;

            armsAnimator.Rebind();
            armsAnimator.Update(0f);

            if (ArmsActionsLayer < 0)
                ArmsActionsLayer = armsAnimator.GetLayerIndex(armsActionsLayer);
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
