using Combat.Stats;

namespace Combat.Weapons
{
    // Builds a weapon's StatContainer from the SAME inputs as WeaponSO.ResolveStats:
    // archetype base + weapon intrinsic deltas, per stat. This is the container-side
    // mirror of ResolveStats used to prove equivalence (Phase 2e) and, later, to
    // become the weapon's real stat source (2f).
    public static class WeaponStatBuilder
    {
        // Populate `container` with a weapon's base stat values (archetype + deltas).
        // Sets each stat's BASE; modifiers (perks/affixes) layer on top later.
        public static void PopulateBases(StatContainer container, WeaponSO weapon, WeaponStatKeys keys)
        {
            if (container == null || weapon == null || weapon.archetype == null || keys == null)
                return;

            var a = weapon.archetype;

            container.SetBase(keys.weaponDamage, a.weaponDamage + weapon.weaponDamageDelta);
            container.SetBase(keys.rpm,          a.roundsPerMinute + weapon.roundsPerMinuteDelta);
            container.SetBase(keys.magazineSize, a.magazineSize + weapon.magazineSizeDelta);
            container.SetBase(keys.reloadTime,   a.reloadTime   + weapon.reloadTimeDelta);
        }
    }
}
