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

        [Header("Feedback Toggles")]
        [Tooltip("Show crosshair hitmarkers for passive status ticks (burns, etc).")]
        [SerializeField] private bool showTickHitmarkers = true;
        [Tooltip("Play hit audio on every status tick (can get noisy).")]
        [SerializeField] private bool playTickAudio = true;


        public void ResolveHit(HitContext ctx)
        {
            if (ctx.Target == null) return;
            if (ctx.Target.IsDying) return;

            // PHASE SORTING: Modifier -> Application -> Reaction, regardless of
            // the order effects sit in the source's list. Stable so same-phase
            // effects keep their authored order.
            var ordered = ctx.Effects.OrderBy(e => (int)e.Phase);

            foreach (var effect in ordered)
                effect.Apply(ctx, this);

            if (ctx.DamageDealt > 0f)
                ShowFeedback(ctx);
        }

        private void ShowFeedback(HitContext ctx)
        {
            bool isTick = ctx.Source == HitSource.StatusTick;

            // Honor the tick toggles — direct hits always show.
            bool showMarker = !isTick || showTickHitmarkers;
            bool playAudio = !isTick || playTickAudio;

            if (showMarker && hitmarkerUI != null)
            {
                // Color priority: kill > type color (from the type's normal gradient).
                Color color = ctx.DamageType != null
                    ? ctx.DamageType.normalGradient.topLeft
                    : Color.white;
                if (ctx.WasKill) color = Color.green;
                hitmarkerUI.ShowHitmarker(color, ctx.WasKill);
            }

            if (playAudio && playerAudio != null)
            {
                // prefer the damage type's own hit sound; fall back to default clip
                AudioClip clip = (ctx.DamageType != null && ctx.DamageType.hitSound != null)
                    ? ctx.DamageType.hitSound
                    : hitmarkerClip;

                if (clip != null)
                {
                    // tick markers are subtler — quieter, no headshot pitch bump
                    float pitch = (!isTick && ctx.WasHeadshot) ? 1.25f : 1f;
                    float volume = isTick ? 0.25f : (ctx.WasHeadshot ? 1f : 0.5f);
                    playerAudio.Play2D(clip, volume, pitch);
                }
            }


            // Kill feedback — now fires for DOT kills too, since ticks route here.
            if (ctx.WasKill && playerAudio != null && killClip != null)
                playerAudio.Play2D(killClip);

            // floating damage number
            if (ctx.ShowFloatingNumber && DamageNumberPool.Instance != null && ctx.DamageDealt > 0f)
            {
                Vector3 pos = (ctx.Target as MonoBehaviour) != null
                ? ((MonoBehaviour)ctx.Target).transform.position + Vector3.up * 2f
                : ctx.HitPoint; 


                DamageNumberPool.Instance.Spawn(
                    pos,
                    ctx.DamageDealt,
                    ctx.DamageType,      // the DamageTypeSO itself
                    ctx.WasHeadshot,     // isCrit (headshot for now)
                    ctx.WasDebuffed);    // isDebuffed test flag
            }

            // rolling accumulator (opt-in per effect)
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
                    ctx.WasHeadshot,
                    ctx.WasDebuffed);
            }

        }

    }
}