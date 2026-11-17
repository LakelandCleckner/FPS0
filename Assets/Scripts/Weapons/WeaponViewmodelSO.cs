using UnityEngine;

namespace Combat.Weapons
{
    // Presentation data for a weapon, kept out of WeaponSO's combat data.
    // WeaponSO.viewmodel references one of these.
    [CreateAssetMenu(fileName = "WeaponViewmodel", menuName = "Combat/Weapons/Weapon Viewmodel")]
    public class WeaponViewmodelSO : ScriptableObject
    {
        [Tooltip("Arms controller for this weapon: its AOC_Arms_* override " +
                 "(or AC_Arms_Base directly).")]
        public RuntimeAnimatorController armsController;

        [Tooltip("Gun model prefab. Spawned onto the rig's hand socket per loadout slot. " +
                 "Must contain its own Animator with the gun override controller.")]
        public GameObject gunPrefab;

        [Tooltip("Third-person body controller: an AOC_TP_* override. Empty = the body's base controller.")]
        public RuntimeAnimatorController thirdPersonController;

        [Header("Sight alignment")]
        [Tooltip("Fine shift of the aimed pose in millimetres (x right, y up), added to the measured sight alignment. " +
                 "Tune live in play mode with the rig's Debug Center Dot on.")]
        public Vector2 adsNudgeMm = Vector2.zero;
        [Tooltip("Tilt of the aimed pose in degrees around the front sight (x pitch, + raises the muzzle; y yaw, + turns right). " +
                 "Use it to centre the front post in the rear sight, then fix the height with the nudge.")]
        public Vector2 adsTiltDeg = Vector2.zero;

        [Header("Recoil (visual)")]
        [Tooltip("Play the arms fire clip. Off = the procedural kick below does the visual recoil.")]
        public bool playArmsFireClip = false;
        [Tooltip("Play the gun model's own fire clip (bolt, slide).")]
        public bool playGunFireClip = true;
        [Tooltip("Push back toward the camera per shot (mm).")]
        public float kickBackMm = 22f;
        [Tooltip("Muzzle rise per shot (deg).")]
        public float kickPitchDeg = 2.5f;
        [Tooltip("Random sideways turn per shot (deg).")]
        public float kickYawDeg = 0.6f;
        [Tooltip("Random roll per shot (deg).")]
        public float kickRollDeg = 1.5f;
        [Tooltip("Kick scale while fully aimed, so the sights stay readable.")]
        [Range(0f, 1f)] public float adsKickScale = 0.35f;
        [Tooltip("Kick scale at 100 stability (0 stability = full kick).")]
        [Range(0f, 1f)] public float kickAtMaxStability = 0.45f;
        [Tooltip("Spring stiffness. Higher snaps back faster.")]
        [Min(1f)] public float springStiffness = 320f;
        [Tooltip("Spring damping. About 2 x sqrt(stiffness) settles without wobble.")]
        [Min(0f)] public float springDamping = 30f;
    }
}
