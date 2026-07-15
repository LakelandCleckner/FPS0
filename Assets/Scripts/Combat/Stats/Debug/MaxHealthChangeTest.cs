using UnityEngine;
using Combat.Core;
using Combat.Stats;

// Simple 2i-a proof: runs all three MaxHealthChangeMode cases ONCE at Start and
// logs the result of each. No keypresses, no per-frame spam. Uses fresh test
// containers so it doesn't disturb the real enemy. Delete after verifying.
public class MaxHealthChangeTest : MonoBehaviour
{
    [SerializeField] private StatDefinitionSO maxHealthStat;  // the max_health stat
    [SerializeField] private float baseMax = 1000f;           // starting max
    [SerializeField] private float bonus = 1f;                // +100% (doubles max)
    [SerializeField, Range(0f, 1f)] private float startFraction = 0.5f; // damage down to this

    private void Start()
    {
        if (maxHealthStat == null)
        {
            Debug.LogError("[MaxHpTest] Assign maxHealthStat.");
            return;
        }

        Debug.Log($"[MaxHpTest] base {baseMax:F0}, +{bonus:P0} max, starting at {startFraction:P0} HP:");
        RunCase("ClampOnly");
        RunCase("Proportional");
        RunCase("Additive");
    }

    private void RunCase(string mode)
    {
        var c = new StatContainer();
        c.SetBase(maxHealthStat, baseMax);

        float oldMax = c.Resolve(maxHealthStat);
        float current = oldMax * startFraction;

        c.AddModifier(new StatModifier(maxHealthStat, StatResolver.ADDITIVE, bonus), owner: this);
        float newMax = c.Resolve(maxHealthStat);

        float result;
        switch (mode)
        {
            case "Proportional":
                result = Mathf.Clamp((current / oldMax) * newMax, 0f, newMax);
                break;
            case "Additive":
                result = Mathf.Clamp(current + (newMax - oldMax), 0f, newMax);
                break;
            default: // ClampOnly
                result = Mathf.Clamp(current, 0f, newMax);
                break;
        }

        Debug.Log($"[MaxHpTest] {mode,-13} | was {current:F0}/{oldMax:F0} -> now {result:F0}/{newMax:F0} ({result / newMax:P0})");
    }
}