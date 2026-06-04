using UnityEngine;
using Combat.Core;

namespace Combat.Core
{
    // WORKING SLICE: guards + single damage effect + feedback.
    // No chain/propagation yet — that's layered on once this vertical is proven.
    public class WeaponHitResolver : MonoBehaviour, IHitResolver
    {
        [SerializeField] private HitmarkerUI hitmarkerUI;
        [SerializeField] private PlayerAudio playerAudio;
        [SerializeField] private AudioClip hitmarkerClip;
        [SerializeField] private AudioClip killClip;

        public void ResolveHit(HitContext ctx)
        {
            // ---- GUARDS ----
            if (ctx.Target == null) return;
            if (ctx.Target.IsDying) return;

            // ---- EFFECTS ----
            // For the slice the effect list runs in insertion order; phases come
            // later. Each effect writes results into ctx.
            foreach (var effect in ctx.Effects)
                effect.Apply(ctx, this);

            // ---- FEEDBACK (always, reads results) ----
            if (ctx.DamageDealt > 0f)
                ShowFeedback(ctx);
        }

        private void ShowFeedback(HitContext ctx)
        {
            // Hitmarker color: white normal, red headshot, green kill
            if (hitmarkerUI != null)
            {
                Color color = ctx.WasHeadshot ? Color.red : Color.white;
                if (ctx.WasKill) color = Color.green;
                hitmarkerUI.ShowHitmarker(color, ctx.WasKill);
            }

            // Hit audio
            if (playerAudio != null && hitmarkerClip != null)
            {
                float pitch = ctx.WasHeadshot ? 1.25f : 1f;
                float volume = ctx.WasHeadshot ? 1f : 0.5f;
                playerAudio.Play2D(hitmarkerClip, volume, pitch);
            }

            // Kill audio
            if (ctx.WasKill && playerAudio != null && killClip != null)
                playerAudio.Play2D(killClip);
        }
    }
}
