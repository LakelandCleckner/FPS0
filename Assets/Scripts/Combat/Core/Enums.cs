namespace Combat.Core
{
    // How an effect's damage value is derived. Drives both the calculation
    // and the default chain-falloff behaviour.
    public enum DamageDerivation
    {
        Flat,                     // a fixed number
        PercentOfWeapon,          // % of source weapon damage   (source-anchored)
        PercentOfCrit,            // % of source crit damage      (source-anchored)
        PercentOfTargetMaxHp,     // % of target max health       (target-anchored)
        PercentOfTargetMissingHp  // % of target missing health   (target-anchored)
    }

    

    // When a derived value is computed.
    public enum DerivationTiming
    {
        SnapshotAtApply, // compute once when the effect lands, store the number
        ComputeLive      // recompute every tick from current stats
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
