namespace Combat.Core
{
    // Immutable snapshot of the stats a damage source had when it produced a
    // hit. Carried UNCHANGED through an entire chain so derivations always
    // compute from pristine values. Falloff is applied separately, never folded
    // back in here.
    public readonly struct StatBlock
    {
        public readonly float WeaponDamage;
        public readonly float CritDamage;
        public readonly float CritChance;
        public readonly float GlobalDamageMultiplier;

        public StatBlock(float weaponDamage, float critDamage, float critChance, float globalMult)
        {
            WeaponDamage = weaponDamage;
            CritDamage = critDamage;
            CritChance = critChance;
            GlobalDamageMultiplier = globalMult;
        }
    }
}
