#if UNITY_EDITOR || STATUS_DEBUG
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Combat.Core;
using Combat.Effects;

namespace Combat.Testing
{
    // THROWAWAY verification for the chain/AoE primitive (piece 1). Drop this on ANY
    // GameObject, set three fields, press the key. No weapon, no effect-list edits, no
    // authored assets — it manufactures a parent HitContext at its own position and
    // fires a blast, so it exercises the queue/pool/drain/dedup in isolation.
    //
    // It uses a FLAT DamageSpec (a constant), which resolves with no attacker or
    // source, so the probe needs nothing but a resolver reference and a layer mask.
    //
    // Delete this file when piece 1 is verified. Piece 2 is the real authored
    // explode-on-kill; the primitive it proves does not change.
    public class ChainTestProbe : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("The scene's WeaponHitResolver — the primitive lives on it.")]
        [SerializeField] private WeaponHitResolver resolver;

        [Header("Blast")]
        [Tooltip("Enemy layer(s) to overlap. Set to your enemy layer.")]
        [SerializeField] private LayerMask enemyLayers;
        [SerializeField] private float radius = 5f;
        [Tooltip("Flat damage per target. Big enough to kill, so you can watch cascades.")]
        [SerializeField] private float flatDamage = 50f;
        [Tooltip("Damage type for the blast (any DamageTypeSO).")]
        [SerializeField] private DamageTypeSO damageType;

        [Header("Mode")]
        [Tooltip("Sibling = one blast, all targets full damage (rocket). " +
                 "Link = chain hops with depth/falloff (lightning).")]
        [SerializeField] private bool useChainLinks = false;
        [Tooltip("Only used for Link mode. Sibling mode ignores depth.")]
        [SerializeField] private int maxChainDepth = 3;
        [SerializeField] private float chainFalloff = 0.7f;

        [Header("Trigger")]
        [SerializeField] private Key fireKey = Key.B;
        [Tooltip("Faction the blast belongs to; NeighbourFinder skips same-faction. " +
                 "Set to the PLAYER's faction so it hits enemies.")]
        [SerializeField] private int sourceFaction = 0;

        private NeighbourFinder finder;
        private readonly List<ICombatant> found = new List<ICombatant>(16);
        private List<IHitEffect> effects;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!kb[fireKey].wasPressedThisFrame) return;
            Fire();
        }

        [ContextMenu("Fire Test Blast")]
        public void Fire()
        {
            if (resolver == null) { Debug.LogError("[ChainTestProbe] No resolver."); return; }

            finder ??= new NeighbourFinder(enemyLayers.value);
            effects ??= new List<IHitEffect>
            {
                new DamageHitEffect(new DamageSpec(flatDamage, damageType))
            };

            // A synthetic PARENT context — the "hit" the blast radiates from. It never
            // resolves itself; it only carries the fields RentSibling/RentLink copy
            // (faction, chain params, position). Depth 0, so siblings stay at 0 and
            // links go to 1.
            var parent = new HitContext
            {
                SourceFaction = sourceFaction,
                DamageType = damageType,
                HitPoint = transform.position,
                ChainDepth = 0,
                MaxChainDepth = useChainLinks ? maxChainDepth : 0,
                ChainFalloff = chainFalloff,
                ChainGrowth = 1f,
            };

            // Shared dedup set for this cascade. Empty — there's no originating victim
            // to seed here (the probe isn't a kill), so every neighbour is fair game.
            var alreadyHit = new HashSet<ICombatant>();

            finder.FindInRadius(transform.position, radius, sourceFaction, alreadyHit, found);
            Debug.Log($"[ChainTestProbe] found {found.Count} target(s) in radius {radius}");
            if (found.Count == 0) return;

            if (useChainLinks)
                resolver.EnqueueChainLinks(parent, found, effects, alreadyHit);
            else
                resolver.EnqueueSiblingHits(parent, found, effects, alreadyHit);

            // NOTE: nothing drains here. The queue drains at the end of the next
            // top-level ResolveHit. For an isolated probe with no other hit happening,
            // that means the blast drains on the FOLLOWING real hit — which is wrong
            // for a standalone test. See TEST_NOTES: the probe calls Drain directly in
            // this isolated case. For the REAL path (piece 2, on-kill inside a
            // resolution) the top-level drain handles it and you must NOT call it here.
            resolver.DebugDrainNow();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
#endif