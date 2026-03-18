using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using GOAPGettingStarted.Goals;
using UnityEngine;

namespace GOAPGettingStarted.Behaviours
{
    [RequireComponent(typeof(AgentBehaviour))]
    [RequireComponent(typeof(GoapActionProvider))]
    public class AgentBrain : MonoBehaviour
    {
        private AgentBehaviour agentBehaviour;
        private GoapActionProvider actionProvider;

        private void Awake()
        {
            agentBehaviour = GetComponent<AgentBehaviour>();
            actionProvider = GetComponent<GoapActionProvider>();
        }

        private void Start()
        {
            if (actionProvider.AgentType == null)
            {
                Debug.LogError("[AgentBrain] AgentType is null — check AgentTypeBehaviour Config and Runner are assigned.");
                return;
            }

            if (actionProvider.Receiver == null)
            {
                Debug.LogError("[AgentBrain] Receiver is null — check AgentBehaviour has its Action Provider Base assigned.");
                return;
            }

            actionProvider.RequestGoal<WanderGoal>();
        }
    }
}