namespace Combat.Core
{
    // One on-hit effect. Damage, burn, slow, explode-on-death — each is a
    // self-contained implementation. The resolver runs a list of these in
    // phase order. Effects route further damage BACK through the resolver
    // (never apply damage directly) so chains and feedback stay unified.
    public interface IHitEffect
    {
        // Which phase this runs in (modifiers before application, reactions after).
        EffectPhase Phase { get; }

        // Whether this effect carries forward to the next chain link.
        // Base case: true. A one-shot "first hit bonus" would be false.
        // Upgrades can flip explode-on-death from false to true for cascades.
        bool PropagatesOnChain { get; }

        // Do the thing. The effect reads/writes the context (damage results,
        // etc) and may call resolver.ResolveHit(...) for splash/secondary hits.
        void Apply(HitContext context, IHitResolver resolver);
    }

    // The resolver seen from an effect's perspective — just an entry point that
    // effects can re-enter for chained/splash damage.
    public interface IHitResolver
    {
        void ResolveHit(HitContext context);
    }
}
