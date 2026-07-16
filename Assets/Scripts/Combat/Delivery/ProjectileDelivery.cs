using UnityEngine;
using Combat.Sources;
using Combat.Core;

namespace Combat.Delivery
{
    // Plain-class projectile delivery (was ProjectileStrategy MonoBehaviour). Built
    // once by ProjectileDeliverySO with resolver + prefab + muzzle injected. Logic
    // unchanged — snapshots the source into each spawned Projectile.
    public class ProjectileDelivery : IDelivery
    {
        private readonly WeaponHitResolver resolver;
        private readonly Projectile projectilePrefab;
        private readonly Transform muzzle;
        private readonly ProjectileConfig config;


        public ProjectileDelivery(WeaponHitResolver resolver, Projectile projectilePrefab,
                                  Transform muzzle, ProjectileConfig config)
        {
            this.resolver = resolver;
            this.projectilePrefab = projectilePrefab;
            this.muzzle = muzzle;
            this.config = config;
        }

        public void Fire(Vector3 origin, Vector3 direction, IDamageSource source)
        {
            Vector3 spawnPos = muzzle != null ? muzzle.position : origin;

            var projectile = Object.Instantiate(projectilePrefab, spawnPos,
                                                Quaternion.LookRotation(direction));

            projectile.Init(
                resolver:       resolver,
                attacker:       source.Attacker,
                damageSource:   source,
                effects:        source.GetEffects(),
                sourceFaction:  source.Faction,
                damageType:     source.BaseDamageType,
                maxChainDepth:  source.MaxChainDepth,
                chainFalloff:   source.ChainFalloff,
                chainGrowth:    source.ChainGrowth,
                dedupMode:      source.DedupMode,
                config:         config.Clone(),   // snapshot — immune to later upgrades
                direction:      direction);
        }
    }
}
