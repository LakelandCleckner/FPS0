using UnityEngine;
using Combat.Stats;

namespace Combat.AI
{
    // Who enemies fight. One shared lookup instead of every enemy and sensor searching by tag.
    public static class AITargets
    {
        private static Transform player;
        private static CombatantStats playerStats;

        public static Transform Player
        {
            get
            {
                if (player == null) Find();
                return player;
            }
        }

        public static CombatantStats PlayerStats
        {
            get
            {
                if (player == null) Find();
                return playerStats;
            }
        }

        public static bool PlayerAlive => Player != null && (playerStats == null || !playerStats.IsDying);

        // Lets the player announce itself (respawn, scene load) instead of being searched for.
        public static void SetPlayer(Transform t)
        {
            player = t;
            playerStats = t != null ? t.GetComponent<CombatantStats>() : null;
        }

        private static void Find()
        {
            var go = GameObject.FindWithTag("Player");
            SetPlayer(go != null ? go.transform : null);
        }
    }
}
