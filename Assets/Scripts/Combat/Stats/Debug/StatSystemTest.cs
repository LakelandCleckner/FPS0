using UnityEngine;

namespace Combat.Stats
{
    // Throwaway Phase 2a verification harness. Put it on a scene object, assign a
    // couple of StatDefinitionSO assets, press play, read the console. Delete once
    // Phase 2a is verified.
    public class StatSystemTest : MonoBehaviour
    {
        [SerializeField] private StatDefinitionSO statA;   // e.g. WeaponDamage (default 0)
        [SerializeField] private StatDefinitionSO statB;   // e.g. CritDamage   (default 0.5)

        private void Start()
        {
            Debug.Log($"[StatTest] Registry initialized={StatRegistry.IsInitialized} count={StatRegistry.Count}");

            if (statA != null)
                Debug.Log($"[StatTest] {statA.id} runtimeIndex={statA.RuntimeIndex} default={statA.defaultValue}");
            if (statB != null)
                Debug.Log($"[StatTest] {statB.id} runtimeIndex={statB.RuntimeIndex} default={statB.defaultValue}");

            var c = new StatContainer();

            if (statA != null) Debug.Log($"[StatTest] {statA.id} base (unset) = {c.GetBase(statA)} (expect default {statA.defaultValue})");
            if (statB != null) Debug.Log($"[StatTest] {statB.id} base (unset) = {c.GetBase(statB)} (expect default {statB.defaultValue})");

            if (statA != null)
            {
                c.SetBase(statA, 12f);
                Debug.Log($"[StatTest] {statA.id} base (set 12) = {c.GetBase(statA)} (expect 12)");
                c.ClearBase(statA);
                Debug.Log($"[StatTest] {statA.id} base (cleared) = {c.GetBase(statA)} (expect default {statA.defaultValue})");
            }
        }
    }
}
