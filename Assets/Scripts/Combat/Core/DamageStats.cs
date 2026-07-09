namespace Combat.Core
{
    // Source-agnostic snapshot of the resolved values a damage instance carries
    // through the pipeline. Replaces the old StatBlock in HitContext / Projectile /
    // EffectStackPool. A weapon fills BaseDamage from its StatContainer; a grenade,
    // hazard, or melee source can fill it directly — nothing here assumes "weapon".
    //
    // Immutable snapshot: resolved once when the damage instance is created and
    // carried unchanged through chains/ticks (same semantics the StatBlock had).
    // Grows as more resolved source-scalars are needed. PLAYER-scope values (crit
    // chance/damage, global bonuses) are NOT here — those are read from the player
    // at resolve time in later phases, not snapshotted onto the source.
    public readonly struct DamageStats
    {
        // The source's resolved base damage — the quantity PercentOfWeapon scales.
        // (Named source-agnostically; "PercentOfWeapon" derivation reads this.)
        public readonly float BaseDamage;

        public DamageStats(float baseDamage = 0f)
        {
            BaseDamage = baseDamage;
        }
    }
}
