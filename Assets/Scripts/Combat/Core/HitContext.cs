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
        public float HitboxMultiplier = 1f;     // headshot/crit multiplier
        public BodyPart BodyPartHit = BodyPart.Torso;

        // how to actually deal damage to the concrete target (set by delivery)
        public System.Action<float> ApplyDamageToTarget;

        //version used by status ticks so resistance/type is respected:
        public System.Action<float, DamageTypeSO> ApplyStatusTickDamage;


        // source snapshot (immutable)
        public StatBlock Stats;

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
        public bool WasCrit;     // inert for now; ready for crit-tick / crit feature later
        public bool WasDebuffed;// settable test variable for now; real "debuffed" trigger defined later


        public bool CanPropagate => ChainDepth < MaxChainDepth;
        public float ChainMultiplier =>
            Mathf.Pow(ChainFalloff, ChainDepth) * Mathf.Pow(ChainGrowth, ChainDepth);

        // presentation policy — does this resolution show a floating number?
        // direct hits default true; statuses will author this in step 2.
        public bool ShowFloatingNumber = true;

    }
}
