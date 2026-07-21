using System;
using System.Collections.Generic;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Sources;

namespace Combat.Events
{
    // PER-OWNER event bus. One of these sits on a combatant root (the player, an
    // enemy) and carries that owner's weapon/player events to its subscribers.
    //
    // DEVIATION FROM DOC 07: doc 07 §2 specifies a single central dispatcher with
    // per-weapon routing layered on top. This is per-owner instead. Rationale:
    //   - Enemy weapons can't cross-talk with the player's perks by construction,
    //     rather than by a faction check on every dispatch.
    //   - Producers find it with GetComponentInParent, so nothing has to be wired
    //     into CombatantStats and there is no static singleton to reset between
    //     scenes or fight over in tests.
    //   - Per-weapon routing (doc 07's actual requirement) still works; it's just a
    //     filter on subscription rather than the primary partition.
    // Doc 07 §2 should be amended to match.
    //
    // Placement: on the same GameObject as (or above) the weapons it serves.
    public class WeaponEventBus : MonoBehaviour
    {
        // Mirrors the chain depth cap in spirit: a perk reacting to a hit can cause
        // a hit, which fires again. Doc 07 requires only that this cannot recurse
        // unbounded; a per-dispatch depth counter is the cheapest mechanism that
        // satisfies it, and unlike a per-perk re-entrancy guard it also covers the
        // A-triggers-B-triggers-A case.
        public const int MaxDepth = 4;

        [Header("Debug")]
        [Tooltip("Log every dispatch. Very noisy — validation only.")]
        [SerializeField] private bool logDispatch = false;

        private readonly struct Subscription
        {
            public readonly Action<WeaponEvent> Handler;
            // null = hear this event from ANY weapon on this owner.
            public readonly WeaponDamageSource WeaponFilter;
            // Which kinds of resolution this subscriber wants. Hit-family events only;
            // ignored for ammo/lifecycle events, which have no HitSource.
            public readonly HitSourceMask Sources;

            public Subscription(Action<WeaponEvent> handler, WeaponDamageSource weaponFilter,
                                HitSourceMask sources)
            {
                Handler = handler;
                WeaponFilter = weaponFilter;
                Sources = sources;
            }
        }

        private readonly Dictionary<WeaponEventType, List<Subscription>> subscriptions
            = new Dictionary<WeaponEventType, List<Subscription>>();

        // Dispatch snapshots into a scratch buffer so a handler may subscribe or
        // unsubscribe mid-dispatch without invalidating the iteration. One buffer
        // per depth level, preallocated, because dispatch can re-enter.
        private readonly List<Subscription>[] scratch = new List<Subscription>[MaxDepth];

        private int depth;
        private bool warnedDepth;

        // ------------------------------------------------------- effect contributors

        // Perks that need to alter the shot currently resolving register here. The
        // resolver asks this list for extra effects before it phase-sorts.
        private readonly List<IHitEffectContributor> contributors = new List<IHitEffectContributor>(4);

        public bool HasContributors => contributors.Count > 0;

        public void RegisterContributor(IHitEffectContributor contributor)
        {
            if (contributor == null || contributors.Contains(contributor)) return;
            contributors.Add(contributor);
        }

        public void UnregisterContributor(IHitEffectContributor contributor)
        {
            if (contributor == null) return;
            contributors.Remove(contributor);
        }

        // Iterated by index over a snapshot-free list: contributors must NOT register
        // or unregister from inside Contribute. That would be a perk mutating the
        // perk set mid-resolution, which is a design error rather than a case to
        // support.
        public void Contribute(HitContext ctx, List<IHitEffect> into)
        {
            for (int i = 0; i < contributors.Count; i++)
            {
                try
                {
                    contributors[i].Contribute(ctx, into);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[WeaponEventBus] Contributor threw: {e}");
                }
            }
        }

        private void Awake()
        {
            for (int i = 0; i < MaxDepth; i++)
                scratch[i] = new List<Subscription>(8);
        }

        // ---------------------------------------------------------------- subscribe

        // weapon == null subscribes to this event from every weapon on this owner.
        // Pass a weapon to get doc 07's per-weapon routing: that gun's perks hear
        // only that gun's events.
        public void Subscribe(WeaponEventType type, Action<WeaponEvent> handler,
                              WeaponDamageSource weapon = null,
                              HitSourceMask sources = HitSourceMask.Direct)
        {
            if (handler == null) return;

            if (!subscriptions.TryGetValue(type, out var list))
            {
                list = new List<Subscription>(4);
                subscriptions[type] = list;
            }
            list.Add(new Subscription(handler, weapon, sources));
        }

        public void Unsubscribe(WeaponEventType type, Action<WeaponEvent> handler,
                                WeaponDamageSource weapon = null)
        {
            if (handler == null) return;
            if (!subscriptions.TryGetValue(type, out var list)) return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Handler == handler && list[i].WeaponFilter == weapon)
                {
                    list.RemoveAt(i);
                    return;
                }
            }
        }

        // Bulk removal when a whole subscriber detaches. Mirrors
        // StatContainer.RemoveAllFromOwner so perk teardown looks the same on both
        // halves of the system.
        public int UnsubscribeAll(object owner)
        {
            if (owner == null) return 0;

            int removed = 0;
            foreach (var kvp in subscriptions)
            {
                var list = kvp.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(list[i].Handler.Target, owner))
                    {
                        list.RemoveAt(i);
                        removed++;
                    }
                }
            }
            return removed;
        }

        // ----------------------------------------------------------------- publish

        public void Publish(in WeaponEvent evt)
        {
            if (!TryEnterScope())
                return;

            try
            {
                if (!subscriptions.TryGetValue(evt.Type, out var list) || list.Count == 0)
                {
                    if (logDispatch)
                        Debug.Log($"[WeaponEventBus] {evt.Type} (depth {depth}) from " +
                                  $"{(evt.Weapon != null ? evt.Weapon.name : "<no weapon>")} " +
                                  "-> 0 subscribers" +
                                  (evt.IsHitEvent ? $" [{evt.Hit.Source}]" : ""));
                    return;
                }

                var buffer = scratch[depth - 1];
                buffer.Clear();

                for (int i = 0; i < list.Count; i++)
                {
                    var sub = list[i];

                    // null filter hears everything; otherwise the weapon must match.
                    if (sub.WeaponFilter != null && sub.WeaponFilter != evt.Weapon)
                        continue;

                    // Hit-family events carry a HitSource; a subscriber only hears the
                    // kinds it asked for. Ammo and lifecycle events have no HitSource
                    // and are never filtered this way.
                    if (evt.IsHitEvent && !sub.Sources.Allows(evt.Hit.Source))
                        continue;

                    buffer.Add(sub);
                }

                // Logged AFTER filtering so the count reflects who actually hears it.
                // A Hit that reaches 0 of 3 subscribers is the HitSourceMask working,
                // not a broken dispatch — without the count those look identical.
                if (logDispatch)
                    Debug.Log($"[WeaponEventBus] {evt.Type} (depth {depth}) from " +
                              $"{(evt.Weapon != null ? evt.Weapon.name : "<no weapon>")} " +
                              $"-> {buffer.Count}/{list.Count} subscribers" +
                              (evt.IsHitEvent ? $" [{evt.Hit.Source}]" : ""));

                for (int i = 0; i < buffer.Count; i++)
                {
                    try
                    {
                        buffer[i].Handler?.Invoke(evt);
                    }
                    catch (Exception e)
                    {
                        // One misbehaving perk must not take out the rest of the
                        // dispatch, or a content bug becomes a combat-wide outage.
                        Debug.LogError($"[WeaponEventBus] Subscriber threw on {evt.Type}: {e}");
                    }
                }

                buffer.Clear();
            }
            finally
            {
                ExitScope();
            }
        }

        // ------------------------------------------------------------- depth guard

        // Exposed so the resolver's PERK INJECTION path shares this counter rather
        // than keeping its own budget. A perk that contributes an effect which causes
        // a hit re-enters ResolveHit, which re-queries contributors. If injection had
        // a separate budget the two paths could ping-pong and neither would notice.
        //
        // IMPORTANT — the resolver holds this scope ONLY while running effects that
        // were actually contributed, NOT for all of ResolveHit. Chains re-enter
        // ResolveHit too, and chain recursion has its own budget (MaxChainDepth). If
        // the scope covered every resolution, a weapon with maxChainDepth above this
        // cap would exhaust the event budget through ordinary chaining and start
        // silently dropping events with no perk involved.
        public bool TryEnterScope()
        {
            if (depth >= MaxDepth)
            {
                if (!warnedDepth)
                {
                    Debug.LogWarning(
                        $"[WeaponEventBus] Event depth cap ({MaxDepth}) hit on '{name}'. " +
                        "A perk chain is recursing — dispatch dropped. " +
                        "This warns once per bus.");
                    warnedDepth = true;
                }
                return false;
            }

            depth++;
            return true;
        }

        public void ExitScope()
        {
            if (depth > 0) depth--;
        }

        public int Depth => depth;

        // ---------------------------------------------------------------- lookup

        // Producers call this in Awake. Returns null (with a warning) if a weapon is
        // sitting outside any owner — worth knowing loudly, since it means that
        // weapon's events silently reach nobody.
        public static WeaponEventBus FindFor(Component producer)
        {
            if (producer == null) return null;

            var bus = producer.GetComponentInParent<WeaponEventBus>();
            if (bus == null)
                Debug.LogWarning(
                    $"[WeaponEventBus] No bus above '{producer.name}'. Its events will " +
                    "not be delivered. Add a WeaponEventBus to the combatant root.");
            return bus;
        }
    }
}