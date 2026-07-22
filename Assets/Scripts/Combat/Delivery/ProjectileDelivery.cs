using UnityEngine;
using Combat.Sources;
using Combat.Core;

namespace Combat.Delivery
{
    // Plain-class projectile delivery (was ProjectileStrategy MonoBehaviour). Built
    // once by ProjectileDeliverySO with resolver + prefab + muzzle injected. Logic
    // unchanged — snapshots the source into each spawned Projectile.
    //
    // Projectiles now come from ProjectilePool rather than Instantiate. Sustained
    // auto-fire was creating and collecting a GameObject per shot, which dwarfed
    // every other per-shot allocation in the combat path.
    //
    // The prefab and config are supplied by ProjectileDeliverySO from a ProjectileSO,
    // so a weapon's projectile TYPE is authored data rather than a scene field on the
    // fire controller — which previously forced every weapon to fire the same one.
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

        public void Fire(Vector3 origin, Vector3 direction, IDamageSource source, in ShotInfo shot)
        {
            Vector3 spawnPos = muzzle != null ? muzzle.position : origin;

            var projectile = ProjectilePool.Instance.Get(
                projectilePrefab, spawnPos, Quaternion.LookRotation(direction));
            if (projectile == null) return;

            projectile.Init(
                resolver: resolver,
                attacker: source.Attacker,
                damageSource: source,
                effects: source.GetEffects(),
                sourceFaction: source.Faction,
                damageType: source.BaseDamageType,
                maxChainDepth: source.MaxChainDepth,
                chainFalloff: source.ChainFalloff,
                chainGrowth: source.ChainGrowth,
                dedupMode: source.DedupMode,
                config: config.Clone(),   // snapshot — immune to later upgrades
                direction: direction,
                // Carried for the whole flight, not per hit: every target a pierce
                // passes through shares this shot's id, which is exactly what stops
                // one round inflating a shot-counting perk by its pierce count.
                shot: shot);
        }
    }
}