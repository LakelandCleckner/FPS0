using System.Linq;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

public class GoapControllerMono : MonoBehaviour, IGoapController
{
    private Goap goap;

    public void Initialize(IGoap goap)
    {
        this.goap = goap as Goap;
    }

    public void OnUpdate()
    {
        if (this.goap == null) return;

        foreach (var kvp in this.goap.AgentTypeRunners)
        {
            kvp.Value.Run(kvp.Key.Agents.All().ToArray());
        }
    }

    public void OnLateUpdate()
    {
        if (this.goap == null) return;

        foreach (var runner in this.goap.AgentTypeRunners.Values)
        {
            runner.Complete();
        }
    }
}