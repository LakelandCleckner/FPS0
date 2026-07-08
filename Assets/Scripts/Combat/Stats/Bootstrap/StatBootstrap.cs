using System.Collections.Generic;
using UnityEngine;

namespace Combat.Stats
{
    // Feeds the StatRegistry its stat definitions at startup. Put one in the scene
    // (e.g. on a GlobalObject that persists) and assign every StatDefinitionSO asset
    // to the list. Runs early so the registry is ready before anything queries stats.
    [DefaultExecutionOrder(-1000)]
    public class StatBootstrap : MonoBehaviour
    {
        [Tooltip("Every StatDefinitionSO in the project. Order here doesn't matter — " +
                 "the registry sorts by id for deterministic indices.")]
        [SerializeField] private List<StatDefinitionSO> statDefinitions = new List<StatDefinitionSO>();

        private void Awake()
        {
            StatRegistry.Initialize(statDefinitions);
        }
    }
}
