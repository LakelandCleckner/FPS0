using UnityEngine;
using Combat.Stats;

namespace Combat.Progression
{
    // The active item scaling curve, set by the scene's StatBootstrap. Edit mode reads it from the scene.
    public static class ItemScaling
    {
        public const int DefaultMaxLevel = 100;

        private static ItemScalingSO active;
        private static bool warned;

        public static ItemScalingSO Active
        {
            get
            {
                if (active == null) active = FromScene();
                return active;
            }
            set => active = value;
        }

        public static int MaxLevel => Active != null ? Active.maxLevel : DefaultMaxLevel;

        public static int Clamp(int level) => Mathf.Clamp(level, 1, MaxLevel);

        // Multiplier on item-level scaled values. x1 with a warning when no curve is set.
        public static float Factor(int level)
        {
            var a = Active;
            if (a != null) return a.Factor(level);
            if (!warned)
            {
                warned = true;
                Debug.LogWarning("[ItemScaling] No Item Scaling asset on the StatBootstrap; item level has no effect.");
            }
            return 1f;
        }

        private static ItemScalingSO FromScene()
        {
            var boot = Object.FindFirstObjectByType<StatBootstrap>();
            return boot != null ? boot.Scaling : null;
        }
    }
}
