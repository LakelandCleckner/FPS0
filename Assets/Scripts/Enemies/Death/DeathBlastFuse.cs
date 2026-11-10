using System.Collections.Generic;
using UnityEngine;
using Combat.Hazards;

namespace Combat.Enemies
{
    // Pulsing warning that counts down a death blast, then detonates it. Pooled.
    public class DeathBlastFuse : MonoBehaviour
    {
        private static readonly Stack<DeathBlastFuse> pool = new Stack<DeathBlastFuse>();

        private DeathBlastSO blast;
        private DeathEffectContext ctx;
        private DeathDamage.Resolved damage;
        private float time;
        private Renderer shape;
        private MaterialPropertyBlock block;

        public static void Begin(DeathBlastSO so, in DeathEffectContext at, DeathDamage.Resolved damage)
        {
            DeathBlastFuse f = null;
            while (pool.Count > 0 && f == null) f = pool.Pop();
            if (f == null)
            {
                f = new GameObject("DeathBlastFuse").AddComponent<DeathBlastFuse>();
                f.block = new MaterialPropertyBlock();
                f.shape = HazardTargets.MakeShape(PrimitiveType.Sphere, "Warning", f.transform);
            }
            f.blast = so;
            f.ctx = at;
            f.damage = damage;
            f.time = 0f;
            f.transform.position = at.Position;
            f.shape.enabled = so.showWarning;
            f.gameObject.SetActive(true);
            f.Pulse();
        }

        private void Update()
        {
            time += Time.deltaTime;
            if (time < blast.fuse) { Pulse(); return; }

            gameObject.SetActive(false);
            pool.Push(this);
            blast.Detonate(ctx, damage);
        }

        // Grows toward the blast radius and flashes faster as it nears zero.
        private void Pulse()
        {
            if (!shape.enabled) return;
            float t = blast.fuse > 0f ? Mathf.Clamp01(time / blast.fuse) : 1f;
            float flash = 0.5f + 0.5f * Mathf.Sin(time * Mathf.Lerp(10f, 40f, t));
            shape.transform.localScale = Vector3.one * (blast.radius * 2f * Mathf.Lerp(0.2f, 0.5f, t));
            var c = blast.Color;
            c.a = Mathf.Lerp(0.2f, 0.8f, flash);
            HazardTargets.Tint(shape, block, c);
        }
    }
}
