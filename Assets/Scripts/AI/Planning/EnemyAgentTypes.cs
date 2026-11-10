using System.Collections.Generic;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

namespace Combat.AI.Goap
{
    // Agent types built in code and registered with the scene's GoapBehaviour on first use.
    public static class EnemyAgentTypes
    {
        public const string Melee = "EnemyMelee";

        private static GoapBehaviour goap;

        public static IAgentType Get(string id)
        {
            if (goap == null) goap = Object.FindFirstObjectByType<GoapBehaviour>();
            if (goap == null)
            {
                Debug.LogError("[EnemyAgentTypes] No GoapBehaviour in the scene.");
                return null;
            }

            // GetAgentType initialises the runner first, then throws if the id is unknown.
            try { return goap.GetAgentType(id); }
            catch (KeyNotFoundException) { }

            var config = Build(id, goap.Config.GoapInjector);
            if (config == null)
            {
                Debug.LogError($"[EnemyAgentTypes] Unknown agent type '{id}'.");
                return null;
            }
            goap.Register(config);
            return goap.GetAgentType(id);
        }

        private static IAgentTypeConfig Build(string id, IGoapInjector injector)
        {
            switch (id)
            {
                case Melee:
                {
                    var b = new AgentTypeBuilder(injector, id);
                    b.AddCapability<PerceptionCapability>();
                    b.AddCapability<MeleeCapability>();
                    b.AddCapability<SearchCapability>();
                    b.AddCapability<IdleCapability>();
                    return b.Build();
                }
                default:
                    return null;
            }
        }
    }
}
