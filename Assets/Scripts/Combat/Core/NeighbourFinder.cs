using System.Collections.Generic;
using UnityEngine;

namespace Combat.Core
{
    // Finds combatants near a point — the targeting half of a chain reaction, kept
    // SEPARATE from the resolve primitive so "who gets hit" composes independently from
    // "resolve the hits". An area effect finds neighbours in a radius; a "nearest 3"
    // arc sorts and takes 3; a single-target perk skips this entirely. All feed the
    // same ChainResolver.EnqueueTargets.
    //
    // Allocation-free per call: the physics overlap uses a preallocated buffer and the
    // result list is caller-supplied and reused.
    public class NeighbourFinder
    {
        private readonly Collider[] overlap;
        private readonly int layerMask;

        public NeighbourFinder(int layerMask, int maxOverlap = 64)
        {
            this.layerMask = layerMask;
            overlap = new Collider[maxOverlap];
        }

        // Fill `results` with combatants within `radius` of `center`, excluding any in
        // `exclude` (the cascade's already-hit set — so we never gather a target the
        // cascade has spent) and excluding by faction relative to `attackerFaction`.
        //
        // `results` is cleared first and reused by the caller across cascades.
        public void FindInRadius(
            Vector3 center,
            float radius,
            int attackerFaction,
            HashSet<ICombatant> exclude,
            List<ICombatant> results)
        {
            results.Clear();

            int count = Physics.OverlapSphereNonAlloc(
                center, radius, overlap, layerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var col = overlap[i];
                if (col == null) continue;

                // A combatant may have many colliders (hitboxes); resolve to the one
                // ICombatant and dedup below, so a multi-hitbox enemy is one neighbour.
                var hitbox = col.GetComponentInParent<EnemyHitbox>();
                var combatant = hitbox != null ? hitbox.combatant as ICombatant : null;
                if (combatant == null) continue;

                if (combatant.IsDying) continue;

                // Faction filter. For now: don't chain onto same-faction targets. When
                // the faction relationship model lands (doc 10 §4) this becomes a
                // relationship query rather than an integer compare.
                if (combatant.Faction == attackerFaction) continue;

                if (exclude != null && exclude.Contains(combatant)) continue;

                // Dedup within this call — multiple colliders, one combatant.
                if (results.Contains(combatant)) continue;

                results.Add(combatant);
            }
        }

        // Nearest-N variant for arc-style chains ("jump to the closest 3"). Finds in
        // radius, then sorts by distance and trims. Reuses the same buffers.
        public void FindNearest(
            Vector3 center,
            float radius,
            int attackerFaction,
            int maxTargets,
            HashSet<ICombatant> exclude,
            List<ICombatant> results)
        {
            FindInRadius(center, radius, attackerFaction, exclude, results);
            if (results.Count <= maxTargets) return;

            results.Sort((a, b) =>
            {
                float da = SqrDist(center, a);
                float db = SqrDist(center, b);
                return da.CompareTo(db);
            });
            results.RemoveRange(maxTargets, results.Count - maxTargets);
        }

        private static float SqrDist(Vector3 center, ICombatant c)
        {
            var mb = c as MonoBehaviour;
            if (mb == null) return float.MaxValue;
            return (mb.transform.position - center).sqrMagnitude;
        }
    }
}