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
    }
}
