using UnityEngine;
using Combat.Delivery;

namespace Combat.Weapons
{
    // A PROJECTILE TYPE as an asset: the prefab plus its movement/impact parameters.
    //
    // Exists because the projectile prefab used to be a serialized field on
    // WeaponFireController, injected through DeliveryBuildContext. That meant every
    // weapon driven by that controller fired the SAME projectile — a grenade launcher
    // and a tracking-missile launcher were not expressible, no matter how their
    // deliveries were authored.
    //
    // With the prefab and config owned by an asset, projectile types are content:
    // grenade, micro-missile, tracking missile, rocket, glaive. Adding one is a new
    // asset, not a code branch. Homing/detonation parameters belong here too as they
    // arrive.
    [CreateAssetMenu(fileName = "Projectile", menuName = "Combat/Weapons/Projectile")]
    public class ProjectileSO : ScriptableObject
    {
        [Tooltip("Stable id for lookups/saves.")]
        public string id = "";
        public string displayName = "";

        [Tooltip("The projectile prefab. Pooled per-prefab at runtime, so different " +
                 "types never share a queue.")]
        public Projectile prefab;

        [Tooltip("Movement and impact parameters for this projectile type.")]
        public ProjectileConfig config = new ProjectileConfig();
    }
}
