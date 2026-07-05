namespace Combat.Core
{
    // Immutable snapshot of the stats a damage source had when it produced a
    // hit. Carried UNCHANGED through an entire chain so derivations always
    // compute from pristine values. Falloff is applied separately, never folded
    // back in here.
    //
    // Composed at hit time from the weapon's resolved stats (archetype base +
    // weapon intrinsic deltas + later perk modifiers). Adding fields is cheap —
    // it's a small readonly struct, queried field-by-field as needed, never
    // iterated hot or boxed.

    public readonly struct StatBlock
    {
        public readonly float WeaponDamage;
        public readonly float CritDamage;
        public readonly float CritChance;
        public readonly float GlobalDamageMultiplier;
        public readonly float RoundsPerMinute;

        // Full constructor (weapon path — RPM matters).
        public StatBlock(float weaponDamage, float critDamage, float critChance,
                         float globalMult, float roundsPerMinute)
        {
            WeaponDamage = weaponDamage;
            CritDamage = critDamage;
            CritChance = critChance;
            GlobalDamageMultiplier = globalMult;
            RoundsPerMinute = roundsPerMinute;
        }

        // Back-compat constructor for call sites that don't care about RPM
        // (status ticks, non-weapon damage). RPM defaults to 0 — harmless for
        // anything that isn't firing a weapon. Keeps existing call sites compiling.
        public StatBlock(float weaponDamage, float critDamage, float critChance,
                         float globalMult)
            : this(weaponDamage, critDamage, critChance, globalMult, 0f) { }
    }
}

