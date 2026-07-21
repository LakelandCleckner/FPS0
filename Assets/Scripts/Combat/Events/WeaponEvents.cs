using System;
using System.Collections.Generic;
using Combat.Core;
using Combat.Effects;
using Combat.Sources;

namespace Combat.Events
{
    // Every event the surface carries. Doc 07 §2 assigns each to ONE authoritative
    // producer — the system that actually knows the thing happened:
    //
    //   Resolver : Hit, PrecisionHit, Kill, PrecisionKill
    //   Weapon   : ShotFired, DryFire, ReloadStart, ReloadComplete, ReloadCancelled,
    //              MagEmpty, MagFull, AmmoChanged, Equip, Stow
    //   Player   : (Dodge, AbilityUsed, DamageTaken — not yet produced)
    //
    // NOTE: there is deliberately NO per-frame / OnUpdate event. Timed perks check
    // their own timers on whatever event they already care about. This is the
    // no-polling principle and it is load-bearing.
    public enum WeaponEventType
    {
        // --- resolver (hit family: Hit is non-null) ---
        Hit,
        PrecisionHit,
        Kill,
        PrecisionKill,

        // --- weapon: firing ---
        ShotFired,
        DryFire,

        // --- weapon: ammo (Magazine/Reserves/MagSize meaningful) ---
        ReloadStart,
        ReloadComplete,
        ReloadCancelled,
        MagEmpty,
        MagFull,
        AmmoChanged,

        // --- weapon: lifecycle ---
        Equip,
        Stow
    }

    // The event payload.
    //
    // Doc 07 wants the context "queryable and lazy — cheap to assemble (references
    // only)". So this is a readonly struct carrying REFERENCES, and hit events carry
    // the live HitContext rather than re-declaring its fields. Target identity,
    // damage dealt, was-crit, body part, damage type and causing source are all
    // already on it; duplicating them would create two descriptions of one hit that
    // can drift apart.
    //
    // WARNING: for hit-family events the HitContext is the LIVE one, still owned by
    // the resolver. READ it, never mutate it. Mutating after resolution breaks doc
    // 02's correctness rule (every multiplier applied before DamageDealt is written,
    // so the floating number can't diverge from real HP loss). Perks that want to
    // change a shot do it by contributing an effect — see IHitEffectContributor.
    //
    // AND DO NOT RETAIN IT. The reference is valid only for the duration of the
    // handler call. EffectStackPool reuses ONE HitContext per status across all its
    // ticks, so a subscriber that stashes evt.Hit to inspect next frame will find it
    // silently overwritten by the next tick — with plausible-looking data from a
    // different hit, which is the worst kind of wrong.
    //
    // If a perk needs hit state later, COPY THE VALUES it cares about (target,
    // damage, was-crit) into its own fields during the handler. Never hold the
    // context itself.
    public readonly struct WeaponEvent
    {
        public readonly WeaponEventType Type;

        // Which weapon produced this, for per-weapon subscription routing. May be
        // null (environmental damage, sourceless hazards).
        public readonly WeaponDamageSource Weapon;

        // Hit family only. Null otherwise.
        public readonly HitContext Hit;

        // Ammo family only. Zero otherwise.
        public readonly int Magazine;
        public readonly int Reserves;
        public readonly int MagSize;

        private WeaponEvent(WeaponEventType type, WeaponDamageSource weapon, HitContext hit,
                            int magazine, int reserves, int magSize)
        {
            Type = type;
            Weapon = weapon;
            Hit = hit;
            Magazine = magazine;
            Reserves = reserves;
            MagSize = magSize;
        }

        public static WeaponEvent ForHit(WeaponEventType type, WeaponDamageSource weapon, HitContext hit)
            => new WeaponEvent(type, weapon, hit, 0, 0, 0);

        public static WeaponEvent ForAmmo(WeaponEventType type, WeaponDamageSource weapon,
                                          int magazine, int reserves, int magSize)
            => new WeaponEvent(type, weapon, null, magazine, reserves, magSize);

        public static WeaponEvent ForWeapon(WeaponEventType type, WeaponDamageSource weapon)
            => new WeaponEvent(type, weapon, null, 0, 0, 0);

        public bool IsHitEvent => Hit != null;
    }

    // WHICH KINDS OF RESOLUTION A SUBSCRIBER WANTS.
    //
    // Every DOT tick and every chain link is its own ResolveHit, so all three
    // produce Hit events. A burning target generates a Hit per tick; a chain
    // weapon generates one per link. A perk that meant "when I shoot something"
    // and subscribed without filtering would ramp continuously off a single burn.
    //
    // Hence the DEFAULT IS Direct ONLY. Forgetting to widen the mask gives you too
    // FEW events, which surfaces as "my perk isn't firing" — visible, and fixed in
    // seconds. Defaulting to All would give too MANY, which surfaces as a balance
    // number being quietly wrong weeks later. Wrong-but-loud beats wrong-but-quiet.
    [Flags]
    public enum HitSourceMask
    {
        None = 0,
        Direct = 1 << 0,   // a shot that hit
        StatusTick = 1 << 1,   // a burn/poison tick
        Chain = 1 << 2,   // a chain link off another hit
        All = Direct | StatusTick | Chain
    }

    public static class HitSourceMaskExtensions
    {
        public static HitSourceMask ToMask(this HitSource source)
        {
            switch (source)
            {
                case HitSource.Direct: return HitSourceMask.Direct;
                case HitSource.StatusTick: return HitSourceMask.StatusTick;
                case HitSource.Chain: return HitSourceMask.Chain;
                default: return HitSourceMask.None;
            }
        }

        public static bool Allows(this HitSourceMask mask, HitSource source)
            => (mask & source.ToMask()) != 0;
    }

    // THE INTERCEPTION SEAM.
    //
    // A perk that must alter the shot currently resolving can't do it from an event —
    // events fire after resolution. It contributes an IHitEffect into the list the
    // resolver is about to run, which then goes through the normal phase order and
    // the normal damage formula. Nothing bypasses DamageHitEffect's arithmetic, so
    // the correctness rule survives.
    //
    // HARD CONSTRAINT — CRIT IS ALREADY DECIDED. RollCrit runs at the top of
    // ResolveHit, BEFORE the effect loop. A contributed effect therefore CANNOT
    // influence crit chance or crit damage for the hit it is contributing to; the die
    // is already cast. "Guaranteed crit on your next precision hit" must be a
    // StatModifier on the player's container applied before the shot (the reactive
    // path), never a contributed effect. A contributed effect can reach damage values,
    // HitboxMultiplier, chain multiplier and status application — not crit.
    public interface IHitEffectContributor
    {
        // Called once per resolution, before phase sorting. Adding nothing is the
        // common case. Implementations must be cheap: this runs on every hit AND
        // every DOT tick.
        void Contribute(HitContext ctx, List<IHitEffect> into);
    }
}