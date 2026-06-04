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
            if (hitmarkerUI != null)
            {
                Color color = ctx.WasHeadshot ? Color.red : Color.white;
                if (ctx.WasKill) color = Color.green;
                hitmarkerUI.ShowHitmarker(color, ctx.WasKill);
            }

            if (playerAudio != null && hitmarkerClip != null)
            {
                float pitch = ctx.WasHeadshot ? 1.25f : 1f;
                float volume = ctx.WasHeadshot ? 1f : 0.5f;
                playerAudio.Play2D(hitmarkerClip, volume, pitch);
            }

            if (ctx.WasKill && playerAudio != null && killClip != null)
                playerAudio.Play2D(killClip);
        }
    }
}