using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Enemies;
using Combat.Stats;
using Combat.Status;
using Object = UnityEngine.Object;

// Simulated targets: two hidden copies of a real enemy (archetype at a rank), parked below the map with only combat parts left.
public partial class PerkLabWindow
{
    private static readonly HashSet<string> SimKeep = new HashSet<string>
    {
        "CombatantStats", "CombatantHealth", "StatusReceiver", "EnemyHitbox", "EnemyShield",
        "EnemyDefinitionApplier", "WeaponEventBus", "WeaponHitResolver"
    };

    private static readonly Vector3 SimOrigin = new Vector3(0f, -2000f, 0f);
    private const float SimSpacing = 1.5f;

    [SerializeField] private EnemyArchetypeSO simArchetype;
    [SerializeField] private EnemyRank simRank;
    private List<EnemyArchetypeSO> archetypes;
    private GameObject simRoot;
    private CombatantStats simA, simB;
    private int simSettleFrames;

    private bool SimReady => simA != null && simB != null && simSettleFrames <= 0;

    private void DrawSimPicker()
    {
        if (archetypes == null) archetypes = Load<EnemyArchetypeSO>().OrderBy(a => a.name).ToList();
        if (simArchetype == null && archetypes.Count > 0) simArchetype = archetypes[0];
        var names = archetypes.Select(a => a.StatSourceLabel).ToArray();
        int cur = Mathf.Max(0, archetypes.IndexOf(simArchetype));
        using (new EditorGUILayout.HorizontalScope())
        {
            int next = EditorGUILayout.Popup("Simulated enemy", cur, names);
            if (archetypes.Count > 0 && next != cur) { simArchetype = archetypes[next]; DestroySim(); }
            int rank = EditorGUILayout.Popup(Mathf.Min((int)simRank, 3), EnemyTierLayers.RankNames, GUILayout.Width(80));
            if (rank != (int)simRank) { simRank = (EnemyRank)rank; DestroySim(); }
            if (GUILayout.Button("Rebuild", GUILayout.Width(70))) DestroySim();
        }
        if (EditorApplication.isPlaying && simA == null && simArchetype != null) BuildSim();
        if (simA != null && simSettleFrames > 0) EditorGUILayout.LabelField("  building...", EditorStyles.miniLabel);
    }

    private void BuildSim()
    {
        var prefab = simArchetype != null ? simArchetype.bodyPrefab : null;
        if (prefab == null) { Log("sim: archetype has no body prefab"); return; }

        simRoot = new GameObject("PerkLab Sim Targets");
        simRoot.SetActive(false);
        simRoot.transform.position = SimOrigin;
        simA = MakeSim(prefab, Vector3.zero, "Sim A");
        simB = MakeSim(prefab, new Vector3(SimSpacing, 0f, 0f), "Sim B");
        simRoot.SetActive(true);
        simSettleFrames = 3;
    }

    // Instantiated under the inactive root so nothing runs before it is stripped
    private CombatantStats MakeSim(GameObject prefab, Vector3 local, string label)
    {
        var go = Object.Instantiate(prefab, simRoot.transform);
        go.name = label;
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.identity;

        var applier = go.GetComponent<EnemyDefinitionApplier>();
        if (applier != null) applier.SetDefinition(new EnemySpawnDef(null, simArchetype, EnemyTierLayers.Compose(EnemyTierLayers.For(simRank), null)));

        // Dependents go first: a component is skipped while another on its object still requires it
        for (bool removed = true; removed;)
        {
            removed = false;
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is Collider || c is Rigidbody) continue;
                if (c is Renderer r) { r.enabled = false; continue; }
                if (SimKeep.Contains(c.GetType().Name) || IsRequired(c)) continue;
                Object.DestroyImmediate(c);
                removed = true;
            }
        }

        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        return go.GetComponent<CombatantStats>();
    }

    // True if another component on the same object has a RequireComponent for this one's type
    private static bool IsRequired(Component c)
    {
        var t = c.GetType();
        foreach (var other in c.GetComponents<Component>())
        {
            if (other == null || other == c) continue;
            foreach (RequireComponent rc in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                if (Needs(rc.m_Type0, t) || Needs(rc.m_Type1, t) || Needs(rc.m_Type2, t)) return true;
        }
        return false;
    }

    private static bool Needs(System.Type req, System.Type t) => req != null && req.IsAssignableFrom(t);

    // Runs from Update: lets the applier set stats, then turns it off so the sims don't count as live enemies
    private void SimTick()
    {
        if (simA == null || simSettleFrames <= 0) return;
        if (--simSettleFrames > 0) return;
        foreach (var a in simRoot.GetComponentsInChildren<EnemyDefinitionApplier>(true)) a.enabled = false;
        ReviveSim(simA); ReviveSim(simB);
    }

    // Full health, no statuses, alive again after a kill
    private static void ReviveSim(CombatantStats c)
    {
        if (c == null) return;
        var h = c.GetComponent<CombatantHealth>();
        if (h != null) h.OnSpawn();
        var s = c.GetComponent<StatusReceiver>();
        if (s == null) return;
        // OnSpawn drops the pools without lifting their stat changes (spawning relies on CombatantStats for that), so lift them first
        var pools = typeof(StatusReceiver).GetField("pools", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(s) as System.Collections.IDictionary;
        if (pools != null) foreach (var p in pools.Values) (p as EffectStackPool)?.ClearAll();
        s.OnSpawn();
    }

    private void DestroySim()
    {
        if (simRoot != null) Object.DestroyImmediate(simRoot);
        simRoot = null; simA = null; simB = null;
    }
}
