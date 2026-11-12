using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Combat.Core;

// Adds a convex hull, cut from the enemy's skinned mesh, under each EnemyHitbox bone and disables the old
// capsule/box there (kept for a future ragdoll). Also adds the BodyBlocker capsule on the root.
public class EnemyHitboxMeshFit : EditorWindow
{
    private const string HullName = "HB_Hull";
    private const string BlockerName = "BodyBlocker";

    [SerializeField] private GameObject enemyRoot;
    [SerializeField] private string meshFolder = "Assets/Prefabs/Enemies/Hitboxes";
    [SerializeField] private float blockerRadius = 0.35f;
    [SerializeField] private float blockerHeight = 0f;
    [SerializeField] private bool disableUncovered = true;
    [SerializeField] private bool splitHandsFeet = true;
    [SerializeField] private GameObject template;
    [SerializeField] private bool removeStray = true;

    [MenuItem("Tools/Enemy Hitbox Mesh Fit")]
    private static void Open() => GetWindow<EnemyHitboxMeshFit>("Enemy Hitbox Mesh Fit");

    private void OnGUI()
    {
        enemyRoot = (GameObject)EditorGUILayout.ObjectField("Enemy Root", enemyRoot, typeof(GameObject), true);
        meshFolder = EditorGUILayout.TextField("Mesh Folder", meshFolder);
        disableUncovered = EditorGUILayout.Toggle(new GUIContent("Disable Hitboxes Without Hull",
            "A hitbox the mesh doesn't skin to (e.g. a whole-body collider on the root) would enclose the hulls and catch every shot first."),
            disableUncovered);
        splitHandsFeet = EditorGUILayout.Toggle(new GUIContent("Separate Hands And Feet",
            "Own hulls on hand and foot bones so they follow the wrist and ankle. They still count as their limb's hitbox."),
            splitHandsFeet);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hitbox bones (new Humanoid body)", EditorStyles.boldLabel);
        template = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Template Enemy",
            "A finished Humanoid enemy (prefab or scene). Its bone hitboxes, ragdoll bodies, joints and colliders are copied by Humanoid bone and rescaled."),
            template, typeof(GameObject), true);
        removeStray = EditorGUILayout.Toggle(new GUIContent("Remove Other Hitboxes",
            "Hitboxes not on a copied bone (e.g. a whole-body one) are removed and their colliders turned off."), removeStray);
        string copyProblem = CopyProblem();
        if (copyProblem != null) EditorGUILayout.HelpBox(copyProblem, MessageType.Info);
        using (new EditorGUI.DisabledScope(copyProblem != null))
            if (GUILayout.Button("Copy Hitbox Bones From Template")) CopyBones();

        string problem = Problem();
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);

        using (new EditorGUI.DisabledScope(problem != null))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hitboxes", EditorStyles.boldLabel);
            if (GUILayout.Button("Build Hulls", GUILayout.Height(28))) BuildHulls();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Hulls")) Select(Hulls().Select(h => (Object)h.gameObject));
            if (GUILayout.Button("Remove Hulls (restore old)")) RemoveHulls(true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Body blocker", EditorStyles.boldLabel);
            blockerRadius = EditorGUILayout.FloatField("Radius (m)", blockerRadius);
            blockerHeight = EditorGUILayout.FloatField(new GUIContent("Height (m)", "0 = the NavMeshAgent height."), blockerHeight);
            if (GUILayout.Button("Add / Update Body Blocker")) AddBlocker();
        }

        EditorGUILayout.HelpBox("Open the BASE enemy prefab in Prefab Mode (variants inherit), drag its root in, " +
                                "Copy Hitbox Bones (new bodies only), Build Hulls, Add Body Blocker, then save the prefab.", MessageType.None);
    }

    private string Problem()
    {
        if (Application.isPlaying) return "Exit Play mode first.";
        if (enemyRoot == null) return "Assign the enemy root (open the prefab in Prefab Mode first).";
        if (EditorUtility.IsPersistent(enemyRoot)) return "Drag the root from the Hierarchy (Prefab Mode or scene), not the Project window.";
        if (enemyRoot.GetComponentInChildren<EnemyHitbox>(true) == null) return "No EnemyHitbox under this root.";
        if (enemyRoot.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) return "No SkinnedMeshRenderer under this root.";
        if (!meshFolder.StartsWith("Assets")) return "Mesh Folder must be under Assets.";
        return null;
    }

    private string CopyProblem()
    {
        if (Application.isPlaying) return "Exit Play mode first.";
        if (enemyRoot == null || EditorUtility.IsPersistent(enemyRoot)) return "Assign the enemy root from the Hierarchy.";
        if (template == null) return "Assign a template enemy to copy hitbox bones from.";
        if (template == enemyRoot) return "Template and enemy are the same object.";
        return null;
    }

    // ===== Hitbox bones =====

    private void CopyBones()
    {
        Undo.SetCurrentGroupName("Copy Hitbox Bones");
        int group = Undo.GetCurrentGroup();
        var report = new List<string>();
        HitboxBoneCopier.Copy(template, enemyRoot, removeStray, report);
        Undo.CollapseUndoOperations(group);
        MarkDirty();

        foreach (var line in report)
        {
            string msg = $"[EnemyHitboxMeshFit] {line.TrimStart('!', '?', ' ')}";
            if (line.StartsWith("!")) Debug.LogError(msg);
            else if (line.StartsWith("?")) Debug.LogWarning(msg);
            else Debug.Log(msg);
        }
        Select(enemyRoot.GetComponentsInChildren<EnemyHitbox>(true).Select(h => (Object)h.gameObject));
    }

    // ===== Hulls =====

    private void BuildHulls()
    {
        Undo.SetCurrentGroupName("Build Enemy Hitbox Hulls");
        int group = Undo.GetCurrentGroup();
        RemoveHulls(false);

        var hitboxes = enemyRoot.GetComponentsInChildren<EnemyHitbox>(true);
        var ownerToAnchor = new Dictionary<Transform, Transform>();
        foreach (var hb in hitboxes) ownerToAnchor[hb.transform] = hb.transform;

        // Hands and feet get their own hulls under the limb's hitbox, so wrists and ankles bend them
        var extras = splitHandsFeet ? HandAndFootBones() : new List<Transform>();
        foreach (var b in extras) ownerToAnchor[b] = b;

        var renderers = enemyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var pieces = HitboxHullCutter.Cut(renderers, ownerToAnchor, enemyRoot.transform);
        string folder = $"{meshFolder}/{enemyRoot.name}";

        var made = new List<string>();
        foreach (var hb in hitboxes)
        {
            if (!pieces.TryGetValue(hb.transform, out var piece)) continue;
            var mesh = HitboxHullCutter.SaveHullMesh(piece, folder, "HB_" + hb.name);
            if (mesh == null) continue;

            var go = new GameObject(HullName);
            Undo.RegisterCreatedObjectUndo(go, "Build Enemy Hitbox Hulls");
            go.layer = hb.gameObject.layer;
            go.transform.SetParent(hb.transform, false);
            HitboxHullCutter.AddHull(go, mesh, false);   // solid: magnetism ignores triggers

            // Old shapes stay for a ragdoll, off for hits
            foreach (var old in hb.GetComponents<Collider>())
            {
                Undo.RecordObject(old, "Build Enemy Hitbox Hulls");
                old.enabled = false;
            }
            made.Add(hb.name);
        }

        int extraCount = 0;
        foreach (var bone in extras)
        {
            if (!pieces.TryGetValue(bone, out var piece)) continue;
            var owner = bone.GetComponentInParent<EnemyHitbox>();
            var mesh = HitboxHullCutter.SaveHullMesh(piece, folder, "HB_" + bone.name);
            if (mesh == null || owner == null) continue;

            var go = new GameObject(HullName);
            Undo.RegisterCreatedObjectUndo(go, "Build Enemy Hitbox Hulls");
            go.layer = owner.gameObject.layer;
            go.transform.SetParent(bone, false);
            HitboxHullCutter.AddHull(go, mesh, false);
            extraCount++;
        }
        if (extraCount > 0) Debug.Log($"[EnemyHitboxMeshFit] {extraCount} hand/foot hull(s) added under their limb hitboxes.");

        AssetDatabase.SaveAssets();
        MarkDirty();
        Undo.CollapseUndoOperations(group);

        var missed = hitboxes.Where(h => !made.Contains(h.name)).ToArray();
        if (disableUncovered && made.Count > 0)
            foreach (var hb in missed)
                foreach (var old in hb.GetComponents<Collider>())
                {
                    Undo.RecordObject(old, "Build Enemy Hitbox Hulls");
                    old.enabled = false;
                }
        MarkDirty();

        string names = string.Join(", ", missed.Select(h => h.name));
        Debug.Log($"[EnemyHitboxMeshFit] Hulls on {made.Count}/{hitboxes.Length} hitbox bones of '{enemyRoot.name}'." +
                  (missed.Length > 0
                      ? $" No vertices for: {names} ({(disableUncovered ? "its collider disabled" : "old collider left on")})."
                      : ""));
        Select(Hulls().Select(h => (Object)h.gameObject));
    }

    // Hand and foot bones inside a limb hitbox that don't have a hitbox of their own
    private List<Transform> HandAndFootBones()
    {
        var result = new List<Transform>();
        var anim = enemyRoot.GetComponentInChildren<Animator>(true);
        if (anim != null && anim.isHuman)
        {
            foreach (var b in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                                      HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var t = anim.GetBoneTransform(b);
                if (t != null) result.Add(t);
            }
        }
        else
        {
            // Generic rig: match bone names ending in LeftHand, RightHand, LeftFoot, RightFoot
            string[] ends = { "lefthand", "righthand", "leftfoot", "rightfoot" };
            foreach (var t in enemyRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (ends.Any(n.EndsWith)) result.Add(t);
            }
        }
        result.RemoveAll(t => t.GetComponent<EnemyHitbox>() != null || t.GetComponentInParent<EnemyHitbox>() == null);
        return result;
    }

    private IEnumerable<MeshCollider> Hulls() =>
        enemyRoot == null ? Enumerable.Empty<MeshCollider>()
                          : enemyRoot.GetComponentsInChildren<MeshCollider>(true).Where(m => m.name == HullName).ToArray();

    private void RemoveHulls(bool restoreOld)
    {
        foreach (var h in Hulls()) Undo.DestroyObjectImmediate(h.gameObject);
        if (restoreOld)
        {
            foreach (var hb in enemyRoot.GetComponentsInChildren<EnemyHitbox>(true))
                foreach (var old in hb.GetComponents<Collider>())
                {
                    Undo.RecordObject(old, "Restore Enemy Hitboxes");
                    old.enabled = true;
                }
        }
        MarkDirty();
    }

    // ===== Body blocker =====

    private void AddBlocker()
    {
        int layer = LayerMask.NameToLayer(PhysicsLayers.BodyBlocker);
        if (layer < 0)
        {
            Debug.LogError($"[EnemyHitboxMeshFit] Add a layer named '{PhysicsLayers.BodyBlocker}' first (Project Settings > Tags and Layers).");
            return;
        }

        var nav = enemyRoot.GetComponent<NavMeshAgent>();
        float height = blockerHeight > 0f ? blockerHeight : (nav != null ? nav.height : 1.8f);

        var t = enemyRoot.transform.Find(BlockerName);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(BlockerName);
            Undo.RegisterCreatedObjectUndo(go, "Add Body Blocker");
            go.transform.SetParent(enemyRoot.transform, false);
        }
        else
        {
            go = t.gameObject;
        }

        Undo.RecordObject(go, "Add Body Blocker");
        go.layer = layer;
        var cap = go.GetComponent<CapsuleCollider>();
        if (cap == null) cap = Undo.AddComponent<CapsuleCollider>(go);
        Undo.RecordObject(cap, "Add Body Blocker");
        cap.isTrigger = false;
        cap.direction = 1;
        cap.radius = blockerRadius;
        cap.height = height;

        // Sized in metres whatever the root's scale
        Vector3 s = enemyRoot.transform.lossyScale;
        go.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
        go.transform.localPosition = new Vector3(0f, height * 0.5f / s.y, 0f);
        go.transform.localRotation = Quaternion.identity;

        MarkDirty();
        Selection.activeGameObject = go;
        Debug.Log($"[EnemyHitboxMeshFit] Body blocker on '{enemyRoot.name}': radius {blockerRadius}, height {height}.");
    }

    // ===== Shared =====

    private void MarkDirty()
    {
        if (enemyRoot == null) return;
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.IsPartOfPrefabContents(enemyRoot)) EditorSceneManager.MarkSceneDirty(stage.scene);
        else if (enemyRoot.scene.IsValid()) EditorSceneManager.MarkSceneDirty(enemyRoot.scene);
    }

    private static void Select(IEnumerable<Object> objects)
    {
        var all = objects.ToArray();
        if (all.Length > 0) Selection.objects = all;
    }
}
