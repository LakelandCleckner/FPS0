using Combat.Feedback;
using Combat.Events;
using Combat.Sources;
using System.Collections.Generic;
using UnityEngine;

namespace Combat.Core
{
    public class WeaponHitResolver : MonoBehaviour, IHitResolver
    {
        [SerializeField] private HitmarkerUI hitmarkerUI;
        [SerializeField] private PlayerAudio playerAudio;
        [SerializeField] private AudioClip hitmarkerClip;
        [SerializeField] private AudioClip killClip;

        [Header("Stats")]
        [Tooltip("crit_chance / crit_damage stat definitions (CombatStatKeys asset).")]
        [SerializeField] private CombatStatKeys statKeys;

        [Header("Feedback Toggles")]
        [Tooltip("Show crosshair hitmarkers for passive status ticks (burns, etc).")]
        [SerializeField] private bool showTickHitmarkers = true;
        [Tooltip("Play hit audio on every status tick (can get noisy).")]
        [SerializeField] private bool playTickAudio = true;

        [Header("Events")]
        [Tooltip("Fire Hit/PrecisionHit even when the hit dealt no damage (fully " +
                 "resisted, immune). ON lets 'on hit, apply a mark' perks work against " +
                 "immune targets; OFF matches the feedback gate exactly. Gameplay call.")]
        [SerializeField] private bool fireHitEventsOnZeroDamage = true;

        [Header("Debug")]
        [Tooltip("Log every resolution, plus delivery-side HitLog lines. Only has an " +
                 "effect when HIT_DEBUG is defined (Player Settings > Scripting Define " +
                 "Symbols); without it the log calls compile out entirely.")]
        [SerializeField] private bool logHits = true;

        private WeaponEventBus bus;

        // Deferred multi-target resolution: AoE blasts and chain links enqueue here
        // and drain after the current TOP-LEVEL resolution finishes its feedback and
        // events, so a splash target's number never beats the primary hit's.
        private ChainResolver chain;

        // ResolveHit RE-ENTERS (chains, splash, an effect that causes another hit),
        // so the working effect list cannot be a single shared buffer. One per
        // nesting level, grown on demand.
        private readonly List<List<IHitEffect>> bufferPool = new List<List<IHitEffect>>(4);
        private int resolveDepth;

        private void Awake()
        {
            bus = WeaponEventBus.FindFor(this);
            chain = new ChainResolver(this);
            HitLog.Enabled = logHits;
        }

        // Lets the inspector toggle mute/unmute live in play mode.
        private void OnValidate() => HitLog.Enabled = logHits;

        public void ResolveHit(HitContext ctx)
        {
            if (ctx.Target == null) { LogSkipped(ctx, "no target"); return; }
            if (ctx.Target.IsDying) { LogSkipped(ctx, "target dying"); return; }

            // CRIT ROLL  once per hit, BEFORE effects apply. UNIVERSAL: every
            // resolution flowing through here can crit (direct hits, DOT ticks,
            // chains) provided it carries the attacker's stats. Each DOT tick is its
            // own ResolveHit, so each tick rolls INDEPENDENTLY. Stat reads are
            // bucket-resolved and cached (version-invalidated), not per-frame recompute.
            //
            // Because this precedes the effect loop, a perk-contributed effect can
            // never influence crit for the hit it joins. See IHitEffectContributor.
            RollCrit(ctx);

            var effects = RentBuffer();
            try
            {
                if (ctx.Effects != null)
                    effects.AddRange(ctx.Effects);

                // PERK INTERCEPTION: contributors may add effects for this hit only.
                //
                // The recursion budget is claimed BEFORE contributing, and held for
                // the whole effect loop. Claiming it first matters: once contributed
                // effects are phase-sorted they're interleaved with the weapon's own,
                // so there'd be no clean way to withdraw them afterwards. If the
                // budget is exhausted we simply don't ask  the hit degrades to the
                // un-perked version and still does its damage, which is safer than
                // dropping the hit.
                //
                // Scope is claimed ONLY when contributors exist. Chains re-enter
                // ResolveHit with their own budget (MaxChainDepth); charging them
                // against the event depth cap would drop events during ordinary
                // chaining with no perk involved.
                bool scoped = false;
                if (bus != null && bus.HasContributors)
                {
                    scoped = bus.TryEnterScope();
                    if (scoped)
                        bus.Contribute(ctx, effects);
                }

                // PHASE SORTING: Modifier -> Application -> Reaction.
                // MUST be a STABLE sort  doc 02 guarantees "a Modifier listed after
                // an Application still runs first", i.e. authored order is preserved
                // within a phase. LINQ OrderBy (the previous implementation) is
                // stable; List<T>.Sort is NOT, so this is a hand-rolled insertion
                // sort rather than the obvious one-liner. Effect lists are tiny, and
                // this also removes the per-hit LINQ allocation that fired on every
                // DOT tick.
                StableSortByPhase(effects);

                try
                {
                    for (int i = 0; i < effects.Count; i++)
                        effects[i].Apply(ctx, this);
                }
                finally
                {
                    if (scoped) bus.ExitScope();
                }
            }
            finally
            {
                ReturnBuffer(effects);
            }

            // After the effect loop, before feedback/events. resolveDepth is already
            // back to THIS call's nesting level, so d1+ lines are nested resolutions
            // (e.g. a burn's instant first tick, which logs before its parent shot).
            LogResolved(ctx);

            if (ctx.DamageDealt > 0f)
                ShowFeedback(ctx);

            // AFTER feedback: this hit's presentation completes before any perk
            // cascade begins, so a perk-caused follow-up can't spawn its damage
            // number ahead of the number for the hit that caused it.
            DispatchHitEvents(ctx);

            // TOP-LEVEL ONLY: drain the deferred AoE/chain queue once the outermost
            // resolution is done. resolveDepth was decremented in ReturnBuffer (via
            // the finally above), so it reads 0 exactly at a genuine top-level
            // completion. A link resolved DURING the drain is itself a top-level
            // ResolveHit (depth 0), so this guard alone can't prevent re-draining --
            // ChainResolver.Drain holds its own draining flag for that. Two guards,
            // two jobs: resolveDepth guards the effect buffer, draining guards the queue.
            if (resolveDepth == 0)
                chain.Drain();
        }

        // ------------------------------------------------------------------ events

        private void DispatchHitEvents(HitContext ctx)
        {
            if (bus == null) return;
            if (ctx.DamageDealt <= 0f && !fireHitEventsOnZeroDamage && !ctx.WasKill) return;

            var weapon = ctx.DamageSource as WeaponDamageSource;

            // Read precision from BodyPartHit, NOT ctx.WasHeadshot. WasHeadshot is
            // written by DamageHitEffect, so a hit carrying only a status-application
            // effect leaves it false even on a genuine headshot. BodyPartHit is set
            // by delivery and is always correct.
            bool precision = ctx.BodyPartHit == BodyPart.Head;

            bus.Publish(WeaponEvent.ForHit(WeaponEventType.Hit, weapon, ctx));

            if (precision)
                bus.Publish(WeaponEvent.ForHit(WeaponEventType.PrecisionHit, weapon, ctx));

            if (ctx.WasKill)
            {
                bus.Publish(WeaponEvent.ForHit(WeaponEventType.Kill, weapon, ctx));
                if (precision)
                    bus.Publish(WeaponEvent.ForHit(WeaponEventType.PrecisionKill, weapon, ctx));
            }
        }

        // ------------------------------------------------------------ chain / AoE

        // ONE delivery striking many targets, all at the parent's depth and full
        // damage (a rocket blast, a grenade). Deferred; drains at top level.
        public void EnqueueSiblingHits(
            HitContext parent,
            IReadOnlyList<ICombatant> targets,
            List<IHitEffect> effects,
            HashSet<ICombatant> sharedAlreadyHit)
            => chain.EnqueueSiblings(parent, targets, effects, sharedAlreadyHit);

        // Successive chain HOPS: depth increments, ChainMultiplier falls off per hop,
        // capped by MaxChainDepth. Lightning arc, explosion cascade.
        public void EnqueueChainLinks(
            HitContext parent,
            IReadOnlyList<ICombatant> targets,
            List<IHitEffect> effects,
            HashSet<ICombatant> sharedAlreadyHit)
            => chain.EnqueueLinks(parent, targets, effects, sharedAlreadyHit);

#if UNITY_EDITOR || STATUS_DEBUG
        // TEST ONLY. The queue normally drains at the end of a top-level ResolveHit,
        // so an isolated probe (no surrounding resolution) would never drain. This
        // forces it. NEVER call from the real path - the automatic top-level drain
        // handles that, and calling it mid-resolution would drain early and reorder
        // feedback, which is the exact bug the deferral prevents.
        public void DebugDrainNow() => chain.Drain();
#endif

        // ------------------------------------------------------------ effect list

        // Insertion sort: stable, allocation-free, and faster than a comparison sort
        // at these list sizes.
        private static void StableSortByPhase(List<IHitEffect> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                var key = list[i];
                int keyPhase = (int)key.Phase;
                int j = i - 1;
                while (j >= 0 && (int)list[j].Phase > keyPhase)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
        }

        private List<IHitEffect> RentBuffer()
        {
            if (resolveDepth >= bufferPool.Count)
                bufferPool.Add(new List<IHitEffect>(8));

            var buffer = bufferPool[resolveDepth];
            buffer.Clear();
            resolveDepth++;
            return buffer;
        }

        private void ReturnBuffer(List<IHitEffect> buffer)
        {
            buffer.Clear();
            if (resolveDepth > 0) resolveDepth--;
        }

        // --------------------------------------------------------------------- crit

        // Roll crit from the ATTACKER's resolved crit_chance; on success set the crit
        // multiplier (1 + resolved crit_damage  the D4 model, base 0.5 => x1.5) and
        // WasCrit. Null-safe: no attacker stats (sourceless hazard) => no crit.
        //
        // Crit is INDEPENDENT of precision (hitbox multiplier): a precision hit that
        // also crits gets BOTH multipliers.
        private void RollCrit(HitContext ctx)
        {
            ctx.CritMultiplier = 1f;
            ctx.WasCrit = false;

            var attackerStats = ctx.Attacker != null ? ctx.Attacker.Stats : null;
            if (attackerStats == null || statKeys == null) return;
            if (statKeys.critChance == null || statKeys.critDamage == null) return;

            float chance = attackerStats.Resolve(statKeys.critChance);

            if (chance <= 0f) return;

            if (Random.value < chance)
            {
                float critDamage = attackerStats.Resolve(statKeys.critDamage);
                ctx.CritMultiplier = 1f + critDamage;
                ctx.WasCrit = true;
            }
        }

        // -------------------------------------------------------------------- debug

        // [Conditional] strips the call AND its argument evaluation at every call site
        // unless HIT_DEBUG is defined, so the StringBuilder work below never exists in
        // a normal build. Read-only: touches nothing the pipeline writes.
        [System.Diagnostics.Conditional("HIT_DEBUG")]
        private void LogResolved(HitContext ctx)
        {
            if (!HitLog.Enabled) return;

            var sb = new System.Text.StringBuilder(200);
            sb.Append("[HitDebug] d").Append(resolveDepth).Append(' ').Append(ctx.Source);
            if (ctx.SourceStatus != null) sb.Append(" (").Append(ctx.SourceStatus.name).Append(')');

            sb.Append(" | ").Append(HitLog.NameOf(ctx.DamageSource))
              .Append(" (").Append(HitLog.NameOf(ctx.Attacker)).Append(") -> ")
              .Append(HitLog.NameOf(ctx.Target));

            sb.Append(" | ").Append(ctx.BodyPartHit)
              .Append(" x").Append(ctx.HitboxMultiplier.ToString("F2"));

            sb.Append(" | crit ");
            if (ctx.WasCrit) sb.Append("Y x").Append(ctx.CritMultiplier.ToString("F2"));
            else sb.Append('N');

            sb.Append(" | ").Append(ctx.DamageType != null ? ctx.DamageType.name : "<no type>");

            sb.Append(" | chain ").Append(ctx.ChainDepth);
            if (ctx.ChainDepth > 0) sb.Append(" x").Append(ctx.ChainMultiplier.ToString("F2"));

            sb.Append(" | dealt ").Append(ctx.DamageDealt.ToString("F1"));
            sb.Append(" | HP ").Append(ctx.Target.CurrentHealth.ToString("F0"))
              .Append('/').Append(ctx.Target.MaxHealth.ToString("F0"));
            sb.Append(" | kill ").Append(ctx.WasKill ? 'Y' : 'N');

            // Ticks carry no hit point.
            if (ctx.Source != HitSource.StatusTick)
                sb.Append(" | pt ").Append(ctx.HitPoint.ToString("F2"));

            // Context = the target, so clicking the log line pings the enemy.
            HitLog.Log(sb.ToString(), ctx.Target as UnityEngine.Object);
        }

        [System.Diagnostics.Conditional("HIT_DEBUG")]
        private void LogSkipped(HitContext ctx, string reason)
        {
            HitLog.Log($"[HitDebug] d{resolveDepth} {ctx.Source} SKIPPED ({reason}) | " +
                       $"{HitLog.NameOf(ctx.DamageSource)} -> {HitLog.NameOf(ctx.Target)}");
        }

        // ----------------------------------------------------------------- feedback

        private void ShowFeedback(HitContext ctx)
        {
            bool isTick = ctx.Source == HitSource.StatusTick;

            bool showMarker = !isTick || showTickHitmarkers;
            bool playAudio = !isTick || playTickAudio;

            if (showMarker && hitmarkerUI != null)
            {
                Color color = ctx.DamageType != null
                    ? ctx.DamageType.normalGradient.topLeft
                    : Color.white;
                if (ctx.WasKill) color = Color.green;
                hitmarkerUI.ShowHitmarker(color, ctx.WasKill);
            }

            if (playAudio && playerAudio != null)
            {
                AudioClip clip = (ctx.DamageType != null && ctx.DamageType.hitSound != null)
                    ? ctx.DamageType.hitSound
                    : hitmarkerClip;

                if (clip != null)
                {
                    // precision still drives the audio bump (a headshot sounds like a headshot)
                    float pitch = (!isTick && ctx.WasHeadshot) ? 1.25f : 1f;
                    float volume = isTick ? 0.25f : (ctx.WasHeadshot ? 1f : 0.5f);
                    playerAudio.Play2D(clip, volume, pitch);
                }
            }

            if (ctx.WasKill && playerAudio != null && killClip != null)
                playerAudio.Play2D(killClip);

            // floating damage number.
            // isCrit styling: a REAL crit OR a headshot lights it up (per current
            // design  headshot and crit share the highlight for now; split later if
            // you want distinct precision styling).
            bool highlight = ctx.WasCrit || ctx.WasHeadshot;

            if (ctx.ShowFloatingNumber && DamageNumberPool.Instance != null && ctx.DamageDealt > 0f)
            {
                Vector3 pos = (ctx.Target as MonoBehaviour) != null
                ? ((MonoBehaviour)ctx.Target).transform.position + Vector3.up * 2f
                : ctx.HitPoint;

                DamageNumberPool.Instance.Spawn(
                    pos,
                    ctx.DamageDealt,
                    ctx.DamageType,
                    highlight,           // isCrit  real crit OR headshot
                    ctx.WasDebuffed);
            }

            if (ctx.FeedsAccumulator && ctx.SourceStatus != null
                && DamageAccumulatorRegistry.Instance != null && ctx.DamageDealt > 0f)
            {
                Transform follow = (ctx.Target as MonoBehaviour)?.transform;
                DamageAccumulatorRegistry.Instance.Report(
                    ctx.Target,
                    ctx.SourceStatus,
                    follow,
                    ctx.DamageDealt,
                    ctx.DamageType,
                    false,
                    false);
            }
        }
    }
}