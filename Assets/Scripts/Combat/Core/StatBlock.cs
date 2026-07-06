namespace Combat.Core
{
    // Immutable snapshot of the stats a damage source had when it produced a hit.
    // Carried UNCHANGED through a chain so derivations compute from pristine values.
    //
    // TEMPORARY SHAPE — heading for a data-driven keyed stat system (see combat
    // GDD 14). This struct is a known stopgap: fine for a handful of stats read in
    // a few places, DO NOT keep growing it indefinitely. When many more stats
    // arrive (handling, range, stability, aim assist, ...), that's the signal to
    // move to keyed/data-driven stats rather than adding more fields here.
    //
    // Every field is DEFAULTED and construction uses NAMED ARGUMENTS, so a caller
    // only specifies the stats it cares about (a status tick doesn't mention RPM,
    // magazine, or reload — they default to 0).
    public readonly struct StatBlock
    {
        public readonly float WeaponDamage;
        public readonly float CritDamage;
        public readonly float CritChance;
        public readonly float GlobalDamageMultiplier;
        public readonly float RoundsPerMinute;
        public readonly float MagazineSize;
        public readonly float ReloadTime;

        public StatBlock(
            float weaponDamage = 0f,
            float critDamage = 0f,
            float critChance = 0f,
            float globalMult = 1f,           // neutral multiplier default
            float roundsPerMinute = 0f,
            float magazineSize = 0f,
            float reloadTime = 0f)
        {
            WeaponDamage = weaponDamage;
            CritDamage = critDamage;
            CritChance = critChance;
            GlobalDamageMultiplier = globalMult;
            RoundsPerMinute = roundsPerMinute;
            MagazineSize = magazineSize;
            ReloadTime = reloadTime;
        }
    }
}