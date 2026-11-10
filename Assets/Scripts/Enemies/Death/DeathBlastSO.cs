using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Hazards;
using Combat.Stats;
using Combat.Status;

namespace Combat.Enemies
{
    // Explodes where the enemy died, after an optional warning fuse.
    [CreateAssetMenu(fileName = "DeathBlast", menuName = "Combat/Enemies/Death Blast")]
    public class DeathBlastSO : EnemyDeathEffectSO
    {
        [Header("Blast")]
        public float radius = 3f;
        [Tooltip("Seconds of pulsing warning before it goes off. 0 = instant.")]
        public float fuse = 0.6f;
        [Tooltip("Pulsing sphere during the fuse.")]
        public bool showWarning = false;
        [Tooltip("Raised off the corpse's feet so the blast centres on the body.")]
        public float heightOffset = 1f;

        [Header("Damage")]
        public DeathDamage damage = new DeathDamage();
        [Tooltip("Damage at the edge as a fraction of the centre.")]
        [Range(0f, 1f)] public float edgeDamage = 0.5f;

        [Header("Status (optional)")]
        public StatusSO applyStatus;

        [Header("Slow (optional)")]
        [Tooltip("Speed taken away from everyone hit (0.4 = 40% slower). 0 = none.")]
        [Range(0f, 0.95f)] public float slow = 0f;
        public float slowDuration = 2f;
        [Tooltip("Stat the slow reduces (move_speed).")]
        public StatDefinitionSO slowStat;

        [Header("Look")]
        [Tooltip("Colour comes from the damage type; this is the fallback, and its alpha sets opacity.")]
        public Color color = new Color(1f, 0.4f, 0.1f, 0.35f);
        public float vfxLifetime = 0.4f;
        [Tooltip("Spawned at the blast. Empty = placeholder sphere.")]
        public GameObject blastVfx;
        [Tooltip("Radius the VFX prefab was built at. 0 = don't scale.")]
        public float vfxReferenceRadius = 0f;
        [Tooltip("Recolour the VFX to the damage type's colour.")]
        public bool tintVfx = true;

        private ApplyStatusHitEffect statusEffect;
        private StatusSO statusFor;

        public override void Trigger(in DeathEffectContext ctx)
        {
            var dmg = damage.Resolve(ctx);
            var at = new DeathEffectContext(ctx.Position + Vector3.up * heightOffset, ctx.Owner, ctx.Resolver);
            if (fuse > 0f) DeathBlastFuse.Begin(this, at, dmg);
            else Detonate(at, dmg);
        }

        public void Detonate(in DeathEffectContext ctx, DeathDamage.Resolved dmg)
        {
            Vector3 c = ctx.Position;
            SpawnVfx(c);
            EnemyRagdoll.Blast(c, radius);

            // Local list: a kill here can set off another death effect mid-loop.
            var found = new List<HazardTargets.Target>(8);
            DeathEffectTargets.Find(c, radius, this, ctx.Owner, found);
            if (found.Count == 0) return;

            if (applyStatus != statusFor) { statusFor = applyStatus; statusEffect = applyStatus != null ? new ApplyStatusHitEffect(applyStatus) : null; }

            for (int i = 0; i < found.Count; i++)
            {
                var t = found[i];
                float k = Mathf.Clamp01(Vector3.Distance(c, t.Point) / Mathf.Max(0.01f, radius));
                var effects = new List<IHitEffect>(2) { new DamageHitEffect(dmg.Spec(Mathf.Lerp(1f, edgeDamage, k))) };
                if (statusEffect != null) effects.Add(statusEffect);
                DeathEffectTargets.Hit(ctx, this, t, effects, damage.type);
                if (slow > 0f && !t.Combatant.IsDying) TimedSlow.Apply(t.Combatant.Stats, slowStat, slow, slowDuration, this);
            }
        }

        // Damage type colour at this asset's opacity.
        public Color Color
        {
            get
            {
                var c = damage.type != null ? damage.type.color : color;
                c.a = color.a;
                return c;
            }
        }

        private void SpawnVfx(Vector3 c)
        {
            if (blastVfx == null) { BlastPlaceholderVfx.Spawn(c, radius, vfxLifetime, Color); return; }
            var fx = Instantiate(blastVfx, c, Quaternion.identity);
            if (tintVfx && damage.type != null) VfxTint.Apply(fx, damage.type.color);
            if (vfxReferenceRadius > 0f) fx.transform.localScale = blastVfx.transform.localScale * (radius / vfxReferenceRadius);
            if (vfxLifetime > 0f) Destroy(fx, Mathf.Max(vfxLifetime, 2f));
        }
    }
}
