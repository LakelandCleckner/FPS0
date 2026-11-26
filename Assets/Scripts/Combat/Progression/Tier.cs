using System;
using UnityEngine;
using Combat.Stats;

namespace Combat.Progression
{
    // The current dungeon tier and its enemy scaling. Set by debug keys for now; runs will set it when they start.
    public static class Tier
    {
        private static int current = 1;
        private static TierScalingSO scaling;

        public static int Current => current;

        // Fired with the new tier.
        public static event Action<int> Changed;

        public static TierScalingSO Scaling
        {
            get
            {
                if (scaling == null) scaling = FromScene();
                return scaling;
            }
            set => scaling = value;
        }

        public static void Set(int tier)
        {
            tier = Mathf.Max(1, tier);
            if (tier == current) return;
            current = tier;
            Changed?.Invoke(current);
        }

        private static TierScalingSO FromScene()
        {
            var boot = UnityEngine.Object.FindFirstObjectByType<StatBootstrap>();
            return boot != null ? boot.TierScaling : null;
        }

        // Fresh tier each play session, even with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            current = 1;
            scaling = null;
            Changed = null;
        }
    }
}
