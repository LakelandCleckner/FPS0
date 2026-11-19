using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Enemies;
using Combat.Stats;
using Combat.Status;
using Object = UnityEngine.Object;

// Simulated targets: two hidden copies of a real enemy variant, parked below the map with only combat parts left.
public partial class PerkLabWindow
{
    private static readonly HashSet<string> SimKeep = new HashSet<string>
    {
        "CombatantStats", "CombatantHealth", "StatusReceiver", "EnemyHitbox", "EnemyShield",
        "EnemyDefinitionApplier", "WeaponEventBus", "WeaponHitResolver"
    };

    private static readonly Vector3 SimOrigin = new Vector3(0f, -2000f, 0f);
    private const float SimSpacing = 1.5f;

    [SerializeField] private EnemyVariantSO simVariant;
    private List<EnemyVariantSO> variants;
    private GameObject simRoot;
    private CombatantStats simA, simB;
    private int simSettleFrames;

    private bool SimReady => simA != null && simB != null && simSettleFrames <= 0;

    private void DrawSimPicker()
    {
        if (variants == null) variants = Load<EnemyVariantSO>().OrderBy(v => v.name).ToList();
        if (simVariant == null && variants.Count > 0) simVariant = variants[0];
        var names = variants.Select(v => v.name).ToArray();
        int cur = Mathf.Max(0, variants.IndexOf(simVariant));
        using (new EditorGUILayout.HorizontalScope())
        {
            int next = EditorGUILayout.Popup("Simulated enemy", cur, names);
            if (variants.Count > 0 && next != cur) { simVariant = variants[next]; DestroySim(); }
            if (GUILayout.Button("Rebuild", GUILayout.Width(70))) DestroySim();
        }
        if (EditorApplication.isPlaying && simA == null && simVariant != null) BuildSim();
        if (simA != null && simSettleFrames > 0) EditorGUILayout.LabelField("  building...", EditorStyles.miniLabel);
    }

    private void BuildSim()
    {
        var prefab = simVariant != null ? simVariant.Prefab : null;
        if (prefab == null) { Log("sim: variant has no prefab"); return; }

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
        if (applier != null) applier.SetVariant(simVariant);

        // Several passes, since a component can't go while another still requires it
        for (int pass = 0; pass < 4; pass++)
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is Collider || c is Rigidbody) continue;
                if (c is Renderer r) { r.enabled = false; continue; }
                if (SimKeep.Contains(c.GetType().Name)) continue;
                try { Object.DestroyImmediate(c); } catch { }
            }

        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        return go.GetComponent<CombatantStats>();
    }

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
        if (s != null) s.OnSpawn();
    }

    private void DestroySim()
    {
        if (simRoot != null) Object.DestroyImmediate(simRoot);
        simRoot = null; simA = null; simB = null;
    }
}
