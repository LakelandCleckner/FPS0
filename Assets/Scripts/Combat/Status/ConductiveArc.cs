using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;

namespace Combat.Status
{
    // Direct hits on a Conductive target arc part of their damage to nearby enemies as chain hits.
    public static class ConductiveArc
    {
        // Arc payloads stay referenced by queued links until the resolver drains, so they are reused round-robin.
        private sealed class Payload
        {
            public readonly HashSet<ICombatant> AlreadyHit = new HashSet<ICombatant>();
            public readonly DamageHitEffect Damage = new DamageHitEffect(default);
            public readonly List<IHitEffect> Effects = new List<IHitEffect>(1);
            public Payload() => Effects.Add(Damage);
        }

        private const int Ring = 32;
        private static readonly Payload[] payloads = new Payload[Ring];
        private static int next;
        private static readonly List<ICombatant> found = new List<ICombatant>(8);
        private static readonly Dictionary<int, NeighbourFinder> finders = new Dictionary<int, NeighbourFinder>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < Ring; i++) payloads[i] = null;
            next = 0;
            found.Clear();
            finders.Clear();
        }

        public static void TryArc(HitContext ctx, WeaponHitResolver resolver)
        {
            if (ctx.Source != HitSource.Direct || ctx.DamageDealt <= 0f || resolver == null) return;
            var receiver = StatusReceiver.For(ctx.Target);
            var status = receiver != null ? receiver.Conductive : null;
            if (status == null) return;

            var mb = ctx.Target as MonoBehaviour;
            if (mb == null) return;
            Vector3 center = mb.transform.position;

            int mask = status.enemyLayers.value;
            if (!finders.TryGetValue(mask, out var finder)) finders[mask] = finder = new NeighbourFinder(mask);

            var p = payloads[next] ??= new Payload();
            next = (next + 1) % Ring;
            p.AlreadyHit.Clear();
            p.AlreadyHit.Add(ctx.Target);

            finder.FindNearest(center, status.arcRadius, ctx.SourceFaction, status.arcTargets, p.AlreadyHit, found);
            if (found.Count == 0) return;

            var type = status.damageType != null ? status.damageType : ctx.DamageType;
            p.Damage.SetSpec(new DamageSpec(ctx.DamageDealt * status.arcFraction, type, false));

            // Arcs are one hop deep whatever the weapon's chain setting.
            if (ctx.MaxChainDepth < ctx.ChainDepth + 1) ctx.MaxChainDepth = ctx.ChainDepth + 1;
            resolver.EnqueueChainLinks(ctx, found, p.Effects, p.AlreadyHit);

            Color color = type != null ? type.color : Color.white;
            Material mat = type != null ? type.arcMaterial : null;
            Vector3 from = center + Vector3.up;
            for (int i = 0; i < found.Count; i++)
                if (found[i] is MonoBehaviour t && t != null)
                    ArcVfx.Draw(from, t.transform.position + Vector3.up, color, mat);
        }
    }
}
