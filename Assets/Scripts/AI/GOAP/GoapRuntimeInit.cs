using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

namespace Combat.Spawning
{
    // Fixes runtime-instantiated GOAP agents. A prefab asset can't serialize a
    // reference to the scene's GoapBehaviour (a scene object), so an instantiated
    // enemy comes up with a null runner on its AgentTypeBehaviour and NREs during
    // Awake. CrashKonijn's own runtime pattern is to skip AgentTypeBehaviour's
    // auto-registration and assign the AgentType in code from FindObjectOfType.
    //
    // This component does that, but it MUST run before GoapActionProvider.Awake reads
    // the (null) type. The only reliable way is: instantiate the prefab INACTIVE, call
    // Initialize() while it's still inactive (no Awake has run yet), THEN activate. The
    // pool does exactly that. Initialize looks up the GoapBehaviour and assigns the
    // AgentType to the provider by name, so when activation finally runs the Awakes,
    // the type is already there.
    //
    // Put this on the enemy prefab root and set the agentTypeName to match the type
    // your FpsAgentTypeFactorySO builds (the string passed to AgentTypeBuilder).
    public class GoapRuntimeInit : MonoBehaviour
    {
        [Tooltip("Must match the AgentType name your factory builds — the string given " +
                 "to AgentTypeBuilder(\"...\") in FpsAgentTypeFactorySO.")]
        [SerializeField] private string agentTypeName = "FpsAgent";

        private GoapActionProvider provider;
        private bool initialized;

        // Scene-placed enemies aren't spawned through the pool, so nothing calls
        // Initialize() on them externally. Self-initialize in Awake — which runs for
        // every component before ANY component's Start, so the AgentType is assigned
        // before GoapActionProvider.Start does its null check. The `initialized` guard
        // makes the pool's explicit Initialize() call (for spawned enemies) a harmless
        // no-op if Awake already ran.
        private void Awake()
        {
            Initialize();
        }

        // Called by the pool for spawned enemies; also called from Awake for
        // scene-placed ones. Idempotent via the initialized guard.
        public void Initialize()
        {
            if (initialized) return;

            provider = GetComponent<GoapActionProvider>();
            if (provider == null)
            {
                Debug.LogError("[GoapRuntimeInit] No GoapActionProvider on this enemy.");
                return;
            }

            var goap = FindFirstObjectByType<GoapBehaviour>();
            if (goap == null)
            {
                Debug.LogError("[GoapRuntimeInit] No GoapBehaviour (GOAP manager) in the scene.");
                return;
            }

            IAgentType type = null;
            try
            {
                type = goap.GetAgentType(agentTypeName);
            }
            catch (System.Collections.Generic.KeyNotFoundException)
            {
                // The id isn't what we guessed. The AgentTypeScriptable has no id field
                // (it's just a capabilities list), so the id comes from elsewhere — the
                // AgentTypeBehaviour GameObject's name, or how it's registered. Rather
                // than guess again, dump every id the runner actually registered so you
                // can copy the right one into agentTypeName.
                Debug.LogError(
                    $"[GoapRuntimeInit] No AgentType id '{agentTypeName}'. In CrashKonijn " +
                    "the id is the NAME OF THE GAMEOBJECT that has the AgentTypeBehaviour " +
                    "(the one whose Config is your FpsAgentTypeFactorySO, under your " +
                    "GOAPManager). Find that GameObject, and put its exact name in " +
                    "agentTypeName.");
                return;
            }

            if (type == null)
            {
                Debug.LogError($"[GoapRuntimeInit] GetAgentType('{agentTypeName}') returned null.");
                return;
            }

            // Assign the type NOW, while inactive. When the object activates, the
            // provider's Awake sees a type already set and doesn't NRE.
            provider.AgentType = type;
            initialized = true;
        }


    }
}