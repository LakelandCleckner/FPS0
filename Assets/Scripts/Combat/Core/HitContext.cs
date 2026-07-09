using System.Collections.Generic;
using UnityEngine;

namespace Combat.Core
{
    // WORKING SLICE version. Same carrier idea, plus the concrete hooks the slice
    // needs to talk to your existing EnemyHealth/EnemyHitbox. Chain fields exist
    // but are inert at depth 0.
    public class HitContext
    {
        // who/what
        public ITargetInfo Target;
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

        // source snapshot (immutable, source-agnostic). Replaces the old StatBlock.
        public DamageStats Stats;

        // The attacker's PLAYER-SCOPE stat container (crit, global damage, ...).
        // Nullable: sourceless damage (hazards) has none; crit/player bonuses then
        // simply don't apply. Carried as a live reference (read fresh at resolution),
        // multi-entity ready — each attacker carries its own.
        public Combat.Stats.StatContainer AttackerStats;


        // effects
        public List<IHitEffect> Effects;

        // chain state (inert at depth 0 for the slice)
        public int ChainDepth = 0;
        public int MaxChainDepth = 0;
        public float ChainFalloff = 1f;
        public float ChainGrowth = 1f;
        public HashSet<ITargetInfo> AlreadyHit = new HashSet<ITargetInfo>();
        public HitDedupMode DedupMode = HitDedupMode.PerShot;

        // mutable results (effects write, feedback reads)
        public float DamageDealt;
        public bool WasKill;
        public bool WasHeadshot;
        public bool WasCrit;     // inert for now; ready for crit feature (Phase 2h)
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