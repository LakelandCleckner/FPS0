using System.Collections.Generic;
using UnityEngine;
namespace Combat.Core
{
    // WORKING SLICE version. Same carrier idea, plus the concrete hooks the slice
    // needs to talk to CombatantHealth/EnemyHitbox. Chain fields exist
    // but are inert at depth 0.
    public class HitContext
    {
        // who/what
        public ICombatant Target;
        public Vector3 HitPoint;
        public int SourceFaction;
        public DamageTypeSO DamageType;
        // What kind of resolution this is. Set by whoever builds the context:
        // delivery -> Direct, status tick -> StatusTick, chain -> Chain.
        public HitSource Source = HitSource.Direct;
        // per-hit info from the hitbox that was struck
        public float HitboxMultiplier = 1f;     // precision (headshot) multiplier
        public BodyPart BodyPartHit = BodyPart.Torso;
        // how to actually deal damage to the concrete target (set by delivery)
        public System.Action<float> ApplyDamageToTarget;
        // version used by status ticks so resistance/type is respected:
        public System.Action<float, DamageTypeSO> ApplyStatusTickDamage;
        public ICombatant Attacker;
        // effects
        public List<IHitEffect> Effects;
        // chain state (inert at depth 0 for the slice)
        public int ChainDepth = 0;
        public int MaxChainDepth = 0;
        public float ChainFalloff = 1f;
        public float ChainGrowth = 1f;
        // Lazily allocated. Every HitContext used to allocate a HashSet at construction
        // whether or not dedup was ever consulted — a per-hit allocation on every bullet
        // and every DOT tick in the game.
        private HashSet<ICombatant> alreadyHit;
        public HashSet<ICombatant> AlreadyHit => alreadyHit ??= new HashSet<ICombatant>();
        // True without forcing allocation — prefer this for read-only checks.
        public bool HasAlreadyHit => alreadyHit != null && alreadyHit.Count > 0;
        // For reused contexts (status ticks).
        public void ResetAlreadyHit() => alreadyHit?.Clear();

        // ------------------------------------------------------------ reuse guard

        // Bumped every time this context is refilled for a NEW resolution.
        //
        // WeaponEvent captures this value at publish time and compares on access, so
        // a subscriber that stashed the context and reads it a frame later gets a
        // loud error instead of plausible-looking data belonging to a different hit.
        // That failure is otherwise silent and effectively undebuggable — the values
        // are all valid, just from the wrong resolution.
        //
        // Freshly-constructed contexts never bump, so the direct-hit path pays
        // nothing and can never false-fire. When direct hits are eventually pooled
        // too, their refill must call this.
        public int Generation { get; private set; }

        // Call from the refill method, NOT from individual call sites — one bump per
        // reuse, in the same place the field resets live, or the two can drift apart.
        public void BumpGeneration() => Generation++;

        public HitDedupMode DedupMode = HitDedupMode.PerShot;
        // Crit multiplier for this hit, rolled ONCE at the resolver. 1 = no crit; on a
        // crit it's (1 + resolved crit_damage). Damage effects multiply by it.
        public float CritMultiplier = 1f;
        // The source that produced this hit (weapon/grenade/hazard). Nullable.
        // Carried so a status application can live-link its entries to the source and
        // read its CURRENT cached stats per tick (GetStats() is version-invalidated).
        // Named DamageSource to avoid colliding with the existing `Source` (HitSource enum).
        public Combat.Sources.IDamageSource DamageSource;
        // mutable results (effects write, feedback reads)
        public float DamageDealt;
        public bool WasKill;
        public bool WasHeadshot;
        public bool WasCrit;     // reset by RollCrit alongside CritMultiplier
        public bool WasDebuffed; // settable test variable for now; real trigger later
        public bool CanPropagate => ChainDepth < MaxChainDepth;
        public float ChainMultiplier =>
            Mathf.Pow(ChainFalloff, ChainDepth) * Mathf.Pow(ChainGrowth, ChainDepth);
        // which status produced this tick (null for direct hits).
        public Combat.Status.StatusSO SourceStatus;
        // presentation flags carried from the status (defaults suit direct hits)
        public bool ShowFloatingNumber = true;
        public bool FeedsAccumulator = false;
    }
}