namespace Combat.Core
{
    // How an effect's damage value is derived (data-driven). A derivation names a
    // KIND, an OWNER scope, and the stat or quantity to read — no per-case members,
    // so new stats/quantities need no new derivation kinds.
    public enum DerivationKind
    {
        Flat,               // a fixed number (the coefficient)
        PercentOfStat,      // % of a stat resolved on the owner's container
        PercentOfQuantity   // % of a runtime quantity (health, ...) on the owner
    }

    // Whose stat/quantity a derivation reads.
    public enum StatScope
    {
        Attacker,   // the combatant producing the hit
        Source,     // the damage source itself (weapon/grenade) — stats only, no health
        Target      // the combatant being hit
    }

    // Runtime quantities a derivation can read — observed live state, NOT modifiable
    // stats. Extensible: add kinds (ammo, stacks, distance, ...) and wire them in
    // DamageSpec.ResolveQuantity.
    public enum QuantityKind
    {
        CurrentHealth,   // combatant.CurrentHealth
        MissingHealth,   // Max - Current
        HealthFraction   // Current / Max  (0..1)
    }

    // The phase an effect runs in. Resolver runs effects in phase order so
    // modifiers adjust values before damage applies, reactions fire after.
    public enum EffectPhase
    {
        Modifier = 0,    // adjust damage/stats before application
        Application = 1, // actually deal damage / apply status
        Reaction = 2     // respond to results (explode-on-death, on-kill)
    }
    // How repeated applications of the same status combine.
    public enum StackingMode
    {
        Ignore, Refresh, StackIntensity, StackMultiplicative, Independent
    }
    // How same-frame multi-hits on one target collapse.
    public enum HitDedupMode { PerShot, PerProjectile, None }
    // What kind of resolution produced this hit — lets feedback distinguish
    // direct shots from passive status ticks and (later) chain links, and
    // render/toggle each independently.
    public enum HitSource { Direct, StatusTick, Chain }
}