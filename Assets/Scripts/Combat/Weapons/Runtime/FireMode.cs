using System;
using System.Collections.Generic;
using UnityEngine;

namespace Combat.Weapons
{
    // A FIRE MODE = one firing behavior + one delivery, composed.
    //
    // WAS an SO (FireModeSO). It isn't any more, and that's the point: as an asset,
    // every behaviour x delivery combination you used needed its own file — "hitscan
    // semi auto", "projectile burst" — which multiplies. Four behaviours and three
    // deliveries is twelve assets that contain nothing but two references each.
    //
    // As a serialized class it's two dropdowns on the archetype you're already
    // editing. Behaviours and deliveries stay independently reusable assets; only the
    // PAIRING stops being one.
    //
    // The pairing itself is kept (rather than putting behaviour and delivery directly
    // on the archetype) because alt-fire needs it: a weapon has a primary mode and a
    // secondary mode, each a complete behaviour+delivery pair.
    [Serializable]
    public class FireMode
    {
        public string displayName = "";

        [Tooltip("WHEN shots happen (semi/auto/burst/charge).")]
        public FireBehaviorSO behavior;

        [Tooltip("HOW the shot reaches the target (hitscan/projectile).")]
        public DeliverySO delivery;

        [Tooltip("Optional per-burst-index delivery overrides. Lets the final round of " +
                 "a burst be a tracking missile while the rest are bullets. Empty for " +
                 "most weapons.")]
        public List<BurstDeliveryOverride> deliveryOverrides = new List<BurstDeliveryOverride>();
    }

    // Swaps delivery for one shot of a burst.
    //
    // Possible because ShotInfo.BurstIndex reaches the controller before it picks a
    // delivery — the behaviour says which shot this is, the mode says what that shot
    // should be fired as.
    [Serializable]
    public class BurstDeliveryOverride
    {
        [Tooltip("Which shot of the burst, 0-based. Use -1 for 'the final shot', which " +
                 "stays correct if the burst count changes.")]
        public int burstIndex = -1;

        public DeliverySO delivery;
    }
}
