using UnityEngine;
using Combat.Stats;

namespace Combat.AI
{
    // Who enemies fight. One shared lookup instead of every enemy and sensor searching by tag.
    public static class AITargets
    {
        private static Transform player;
        private static CombatantStats playerStats;
        private static CharacterController playerController;

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
            playerController = t != null ? t.GetComponent<CharacterController>() : null;
        }

        // Ground point of a target; the player's root sits at its controller's centre, so drop to the controller's bottom
        public static Vector3 FeetOf(Transform t)
        {
            if (t == null) return Vector3.zero;
            if (t == Player && playerController != null)
                return t.TransformPoint(playerController.center) - Vector3.up * (playerController.height * 0.5f * t.lossyScale.y);
            return t.position;
        }

        private static void Find()
        {
            var go = GameObject.FindWithTag("Player");
            SetPlayer(go != null ? go.transform : null);
        }
    }
}
