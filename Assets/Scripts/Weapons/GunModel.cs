using UnityEngine;

namespace Combat.Weapons
{
    // Sits on the root of a gun model prefab. The loadout spawns the prefab at
    // runtime, so scene-side components can't reference its parts directly — this
    // is how the spawned model hands them over.
    public class GunModel : MonoBehaviour
    {
        [Tooltip("Empty at the barrel tip. Projectiles and muzzle effects spawn here.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Empty at the front sight post (or the sight's aim point). At full ADS " +
                 "the rig shifts the viewmodel so this sits exactly on screen center.")]
        [SerializeField] private Transform sight;

        public Transform Muzzle => muzzle;
        public Transform Sight => sight;
    }
}