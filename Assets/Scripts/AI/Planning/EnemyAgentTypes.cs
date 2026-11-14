using System.Collections.Generic;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

namespace Combat.AI.Goap
{
    // One agent type per behaviour profile, built in code from its capability flags and
    // registered with the scene's GoapBehaviour on first use.
    public static class EnemyAgentTypes
    {
        // Used when an enemy has no profile: melee, search, idle.
        public const string Default = "Enemy_Default";

        private static GoapBehaviour goap;

        public static IAgentType Get(EnemyBehaviourProfileSO profile)
        {
            if (goap == null) goap = Object.FindFirstObjectByType<GoapBehaviour>();
            if (goap == null)
            {
                Debug.LogError("[EnemyAgentTypes] No GoapBehaviour in the scene.");
                return null;
            }

            string id = profile != null ? profile.AgentTypeId : Default;

            // GetAgentType initialises the runner first, then throws if the id is unknown.
            try { return goap.GetAgentType(id); }
            catch (KeyNotFoundException) { }

            goap.Register(Build(id, profile, goap.Config.GoapInjector));
            return goap.GetAgentType(id);
        }

        private static IAgentTypeConfig Build(string id, EnemyBehaviourProfileSO p, IGoapInjector injector)
        {
            bool melee = p == null || p.melee;
            bool kamikaze = p != null && p.kamikaze;
            bool cover = p != null && (p.shieldCover || p.ranged);
            bool ranged = p != null && p.ranged;
            bool search = p == null || p.search;
            bool idle = p == null || p.idle;

            var b = new AgentTypeBuilder(injector, id);
            b.AddCapability<PerceptionCapability>();
            if (melee) b.AddCapability<MeleeCapability>();
            if (kamikaze) b.AddCapability<KamikazeCapability>();
            if (cover) b.AddCapability<ShieldCoverCapability>();
            if (ranged) b.AddCapability<RangedCapability>();
            if (ranged && p.useCover) b.AddCapability<RangedCoverCapability>();
            if (search) b.AddCapability<SearchCapability>();
            if (idle) b.AddCapability<IdleCapability>();
            return b.Build();
        }
    }
}
