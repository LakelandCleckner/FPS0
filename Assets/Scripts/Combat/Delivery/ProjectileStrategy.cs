using UnityEngine;
using Combat.Core;
using Combat.Sources;

namespace Combat.Delivery
{
    // Spawns a Projectile and hands it a full snapshot of the source's current
    // state. The source may change afterward; the projectile is unaffected.
    public class ProjectileStrategy : MonoBehaviour, IFireStrategy
    {
        [SerializeField] private WeaponHitResolver resolver;
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private Transform muzzle; // spawn point; falls back to origin

        public void Fire(Vector3 origin, Vector3 direction, IDamageSource source)
        {
            Vector3 spawnPos = muzzle != null ? muzzle.position : origin;

            var projectile = Instantiate(projectilePrefab, spawnPos,
                                         Quaternion.LookRotation(direction));

            // Pull a projectile config from the source. If the source is also a
            // projectile source it provides one; otherwise use a default.
            var config = (source as IProjectileSource)?.GetProjectileConfig()
                         ?? new ProjectileConfig();

            projectile.Init(
                resolver:       resolver,
                stats:          source.GetStats(),
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
