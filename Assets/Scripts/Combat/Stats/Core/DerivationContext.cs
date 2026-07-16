using Combat.Sources;

namespace Combat.Core
{
    // Everything a derivation might need to resolve, gathered for one computation.
    // Built by a damage effect from a HitContext, or by a status entry from its
    // stored refs. Any field may be null (sourceless hazard, no attacker, etc.);
    // resolution is null-safe (missing scope -> 0).
    public readonly struct DerivationContext
    {
        public readonly ICombatant Attacker;   // Attacker scope (nullable)
        public readonly IDamageSource Source;  // Source scope — stats only (nullable)
        public readonly ICombatant Target;     // Target scope

        public DerivationContext(ICombatant attacker, IDamageSource source, ICombatant target)
        {
            Attacker = attacker;
            Source = source;
            Target = target;
        }
    }
}
