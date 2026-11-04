using UnityEngine;

namespace Combat.Core
{
    // Layer masks shared by shots and line-of-sight casts
    public static class PhysicsLayers
    {
        // Upright capsules that stop the player walking through bodies; never stop bullets
        public const string BodyBlocker = "BodyBlocker";

        private static int blockerMask = -1;

        public static int BlockerMask
        {
            get
            {
                if (blockerMask < 0) blockerMask = LayerMask.GetMask(BodyBlocker);
                return blockerMask;
            }
        }

        // A cast mask with body blockers removed
        public static int ShotMask(int mask) => mask & ~BlockerMask;
    }
}
