using UnityEngine;

namespace Combat.Weapons
{
    // How the third-person body holds the gun. Auto and One Hand keep the authored grip; Two Hand turns the barrel onto the animated left hand.
    public enum TpHold { Auto, TwoHand, OneHand }

    // Sits on the root of a gun model prefab. The loadout spawns the prefab at
    // runtime, so scene-side components can't reference its parts directly - this
    // is how the spawned model hands them over.
    public class GunModel : MonoBehaviour
    {
        [Tooltip("Empty at the barrel tip. Projectiles and muzzle effects spawn here.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Empty at the front sight post (or the sight's aim point). At full ADS " +
                 "the rig shifts the viewmodel so this sits exactly on screen center.")]
        [SerializeField] private Transform sight;

        [Header("Third person (authored with Tools > TP Grip Tool)")]
        [Tooltip("Where the third-person right hand holds the gun.")]
        [SerializeField] private Transform tpGrip;
        [Tooltip("Where the third-person left hand rests.")]
        [SerializeField] private Transform tpSupport;
        [Tooltip("Mag well, used by the third-person reload.")]
        [SerializeField] private Transform tpMagWell;
        [Tooltip("Shell loading port, used by the third-person shell reload.")]
        [SerializeField] private Transform tpLoadPort;
        [Tooltip("World model scale relative to the prefab.")]
        [SerializeField] private float tpScale = 1f;
        [Tooltip("Barrel direction in the gun's local space; the body aims this down the camera ray.")]
        [SerializeField] private Vector3 tpAimAxis = Vector3.forward;
        [Tooltip("Auto / One Hand: fixed at the authored grip (matches the TP hold pose). Two Hand: barrel turns onto the animated left hand, for clips that don't match the grip.")]
        [SerializeField] private TpHold tpHold = TpHold.Auto;
        [Tooltip("Pull the left palm onto TP_Support with its rotation. Off: the hold animation's own left hand, kept on the gun through kick, lower and reload roll.")]
        [SerializeField] private bool tpLeftHandIK = false;

        public Transform Muzzle => muzzle;
        public Transform Sight => sight;

        public Transform TpGrip => tpGrip;
        public Transform TpSupport => tpSupport;
        public Transform TpMagWell => tpMagWell;
        public Transform TpLoadPort => tpLoadPort;
        public float TpScale => tpScale > 0f ? tpScale : 1f;
        public Vector3 TpAimAxis => tpAimAxis.sqrMagnitude > 1e-6f ? tpAimAxis.normalized : Vector3.forward;
        public TpHold TpHold => tpHold;
        public bool TpLeftHandIK => tpLeftHandIK;
    }
}
