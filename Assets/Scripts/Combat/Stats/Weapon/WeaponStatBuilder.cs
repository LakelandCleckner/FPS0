using Combat.Stats;

namespace Combat.Weapons
{
    // Builds a weapon's StatContainer from the SAME inputs as WeaponSO.ResolveStats:
    // archetype base + weapon intrinsic deltas, per stat. This is the container-side
    // mirror of ResolveStats used to prove equivalence (Phase 2e) and, later, to
    // become the weapon's real stat source (2f).
    //
    // Also registers the HANDLING DERIVATIONS: equip_time and stow_time are authored
    // per archetype as real seconds, then reduced proportionally by the weapon's
    // handling. Two levers, doing different jobs — see RegisterHandlingDerivations.
    public static class WeaponStatBuilder
    {
        // ---------------------------------------------------------------- tuning
        //
        // Handling points -> proportional time reduction. At 100 handling a weapon
        // readies in half its authored time; at 0 it takes exactly its authored time.
        //
        // Global for now. If a rocket launcher should get more out of each handling
        // point than a sidearm does, this becomes a `handlingScale` field on the
        // archetype and these constants become its default. One-field upgrade.
        private const float EquipHandlingCoefficient = -0.005f;
        private const float StowHandlingCoefficient = -0.005f;

        // Populate `container` with a weapon's base stat values (archetype + deltas)
        // and register its derived modifiers. Sets each stat's BASE; modifiers
        // (perks/affixes) layer on top later.
        public static void PopulateBases(StatContainer container, WeaponSO weapon, WeaponStatKeys keys)
        {
            if (container == null || weapon == null || weapon.archetype == null || keys == null)
                return;

            var a = weapon.archetype;

            container.SetBase(keys.weaponDamage, a.weaponDamage + weapon.weaponDamageDelta);
            container.SetBase(keys.rpm, a.roundsPerMinute + weapon.roundsPerMinuteDelta);
            container.SetBase(keys.magazineSize, a.magazineSize + weapon.magazineSizeDelta);
            container.SetBase(keys.reloadTime, a.reloadTime + weapon.reloadTimeDelta);

            // Handling is a stat like any other: archetype base plus a per-weapon
            // delta, so two guns on one frame can differ in feel without needing
            // their own equip/stow authoring.
            container.SetBase(keys.handling, a.handling + weapon.handlingDelta);

            // Equip/stow are authored in SECONDS on the archetype and have no
            // per-weapon delta on purpose. If one gun in a frame should be snappier,
            // give it handling — that keeps a single legible knob, and means armour
            // and perks reach it through the same channel.
            container.SetBase(keys.equipTime, a.baseEquipTime);
            container.SetBase(keys.stowTime, a.baseStowTime);

            RegisterHandlingDerivations(container, weapon, keys);
        }

        // WHY PROPORTIONAL (ADDITIVE) AND NOT FLAT SECONDS:
        //
        // A flat "-0.4s at 100 handling" is the same 0.4s whether the weapon takes
        // 1.2s or 0.35s to ready. It barely touches the rocket launcher and drives
        // the sidearm straight into its clamp. A proportional reduction keeps the
        // frame identity intact — a heavy weapon stays heavy at max handling, just
        // less so — which is the whole reason equip time is authored per archetype.
        //
        // equip_time is ScaledBase, so ADDITIVE lands as:
        //     (base + flat) x (1 + sum(additive)) x prod(mult)
        // with sum(additive) = -0.5 at 100 handling, i.e. half the authored time.
        private static void RegisterHandlingDerivations(
            StatContainer container, WeaponSO weapon, WeaponStatKeys keys)
        {
            if (keys.handling == null) return;

            // Idempotent: re-registering on Rebuild would otherwise stack a second
            // copy of each derivation and double the reduction.
            container.RemoveAllFromOwner(weapon);

            if (keys.equipTime != null)
            {
                container.AddModifier(new StatModifier(
                    keys.equipTime,
                    StatResolver.ADDITIVE,
                    sourceStat: keys.handling,
                    coefficient: EquipHandlingCoefficient), weapon);
            }

            if (keys.stowTime != null)
            {
                container.AddModifier(new StatModifier(
                    keys.stowTime,
                    StatResolver.ADDITIVE,
                    sourceStat: keys.handling,
                    coefficient: StowHandlingCoefficient), weapon);
            }

            // NOTE — DELIBERATELY NO CAP ON THESE MODIFIERS.
            //
            // StatModifier's cap is min(Cap, contribution), which is an UPPER bound.
            // That works for D4-style positive derivations ("up to +40%") and does
            // the wrong thing for a negative one: a -0.8 contribution against a -0.5
            // cap yields min(-0.5, -0.8) = -0.8 (uncapped), while a -0.3 contribution
            // yields min(-0.5, -0.3) = -0.5, i.e. the cap makes it STRONGER.
            //
            // The floor that actually matters is on the stat: equip_time and
            // stow_time carry a min clamp (0.05), applied last in StatResolver.
            // Resolve, so no amount of handling can drive a swap to zero. Set those
            // clamps on the definitions — they are the guard here.
        }
    }
}