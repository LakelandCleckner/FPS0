using Combat.Feedback;
using System.Linq;
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

        public void ResolveHit(HitContext ctx)
        {
            if (ctx.Target == null) return;
            if (ctx.Target.IsDying) return;

            // CRIT ROLL — once per hit, BEFORE effects apply. UNIVERSAL: every
            // resolution flowing through here can crit (direct hits, DOT ticks,
            // chains) provided it carries the attacker's stats. Each DOT tick is its
            // own ResolveHit, so each tick rolls INDEPENDENTLY. Stat reads are
            // bucket-resolved and cached (version-invalidated), not per-frame recompute.
            RollCrit(ctx);

            // PHASE SORTING: Modifier -> Application -> Reaction.
            var ordered = ctx.Effects.OrderBy(e => (int)e.Phase);

            foreach (var effect in ordered)
                effect.Apply(ctx, this);

            if (ctx.DamageDealt > 0f)
                ShowFeedback(ctx);
        }

        // Roll crit from the ATTACKER's resolved crit_chance; on success set the crit
        // multiplier (1 + resolved crit_damage — the D4 model, base 0.5 => x1.5) and
        // WasCrit. Null-safe: no attacker stats (sourceless hazard) => no crit.
        //
        // Crit is INDEPENDENT of precision (hitbox multiplier): a precision hit that
        // also crits gets BOTH multipliers.
        private void RollCrit(HitContext ctx)
        {
            ctx.CritMultiplier = 1f;

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
            // design — headshot and crit share the highlight for now; split later if
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
                    highlight,           // isCrit — real crit OR headshot
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
                    highlight,
                    ctx.WasDebuffed);
            }
        }
    }
}