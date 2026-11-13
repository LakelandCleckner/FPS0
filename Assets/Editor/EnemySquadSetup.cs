using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Combat.AI;
using Combat.Enemies;
using Combat.Spawning;

// Build squad makeups (enemy type, rank, count, leader), spawn them in Play, group scene enemies, and watch live squads.
public class EnemySquadSetup : EditorWindow
{
    private const string SquadFolder = "Assets/GameData/Enemies/Squads";
    private static readonly string[] RankNames = { "Minor", "Major", "Elite", "Miniboss" };
    private const float PlayerSpawnDistance = 8f;

    // Variants grouped by enemy type, one slot per rank
    private class EnemyType
    {
        public string Label;
        public readonly EnemyVariantSO[] ByRank = new EnemyVariantSO[4];
    }

    private readonly List<EnemyType> types = new List<EnemyType>();
    private string[] typeLabels = new string[0];
    private List<EnemySquadSO> squads = new List<EnemySquadSO>();
    private EnemySquadSO current;
    private Vector2 listScroll, editScroll;

    [MenuItem("Tools/Enemy Squad Setup")]
    private static void Open() => GetWindow<EnemySquadSetup>("Enemy Squads");

    private void OnEnable() => Refresh();
    private void OnFocus() => Refresh();
    private void OnProjectChange() { Refresh(); Repaint(); }

    private void OnInspectorUpdate()
    {
        if (Application.isPlaying) Repaint();
    }

    private void Refresh()
    {
        BuildTypes();
        squads = AssetDatabase.FindAssets("t:EnemySquadSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<EnemySquadSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(s => s != null).OrderBy(s => s.name).ToList();
        if (current == null || !squads.Contains(current)) current = squads.FirstOrDefault();
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawSquadList();
        EditorGUILayout.BeginVertical();
        editScroll = EditorGUILayout.BeginScrollView(editScroll);
        DrawEditor();
        EditorGUILayout.Space();
        DrawSpawn();
        EditorGUILayout.Space();
        DrawScene();
        EditorGUILayout.Space();
        DrawLive();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    // ---- squad list ----

    private void DrawSquadList()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(180));
        EditorGUILayout.LabelField("Squads", EditorStyles.boldLabel);
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        foreach (var s in squads)
        {
            var style = s == current ? EditorStyles.toolbarButton : EditorStyles.label;
            if (GUILayout.Button($"{s.name}  ({s.TotalCount})", style)) { current = s; GUI.FocusControl(null); }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New")) NewSquad();
        using (new EditorGUI.DisabledScope(current == null))
        {
            if (GUILayout.Button("Copy")) DuplicateSquad();
            if (GUILayout.Button("Delete")) DeleteSquad();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void NewSquad()
    {
        EnsureFolder();
        var asset = CreateInstance<EnemySquadSO>();
        string path = AssetDatabase.GenerateUniqueAssetPath(SquadFolder + "/Squad_New.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        Refresh();
        current = asset;
    }

    private void DuplicateSquad()
    {
        string from = AssetDatabase.GetAssetPath(current);
        string to = AssetDatabase.GenerateUniqueAssetPath(from);
        if (!AssetDatabase.CopyAsset(from, to)) return;
        Refresh();
        current = AssetDatabase.LoadAssetAtPath<EnemySquadSO>(to);
    }

    private void DeleteSquad()
    {
        if (!EditorUtility.DisplayDialog("Delete squad", $"Delete '{current.name}'?", "Delete", "Cancel")) return;
        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(current));
        current = null;
        Refresh();
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(SquadFolder)) return;
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(SquadFolder).Replace('\\', '/'), System.IO.Path.GetFileName(SquadFolder));
    }

    // ---- makeup editor ----

    private void DrawEditor()
    {
        EditorGUILayout.LabelField("Makeup", EditorStyles.boldLabel);
        if (current == null) { EditorGUILayout.HelpBox("No squad selected. Press New.", MessageType.None); return; }
        if (types.Count == 0) { EditorGUILayout.HelpBox("No enemy variants found in the project.", MessageType.Warning); return; }

        string newName = EditorGUILayout.DelayedTextField("Name", current.name);
        if (newName != current.name && !string.IsNullOrWhiteSpace(newName))
        {
            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(current), newName.Trim());
            Refresh();
        }

        var so = new SerializedObject(current);
        var members = so.FindProperty("members");

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Enemy", EditorStyles.miniBoldLabel, GUILayout.MinWidth(120));
        EditorGUILayout.LabelField("Rank", EditorStyles.miniBoldLabel, GUILayout.Width(80));
        EditorGUILayout.LabelField("Count", EditorStyles.miniBoldLabel, GUILayout.Width(45));
        EditorGUILayout.LabelField("Lead", EditorStyles.miniBoldLabel, GUILayout.Width(34));
        GUILayout.Space(24);
        EditorGUILayout.EndHorizontal();

        int remove = -1;
        for (int i = 0; i < members.arraySize; i++)
        {
            var m = members.GetArrayElementAtIndex(i);
            var variantProp = m.FindPropertyRelative("variant");
            var countProp = m.FindPropertyRelative("count");
            var leaderProp = m.FindPropertyRelative("leader");

            Locate(variantProp.objectReferenceValue as EnemyVariantSO, out int type, out int rank);

            EditorGUILayout.BeginHorizontal();
            int newType = EditorGUILayout.Popup(Mathf.Max(0, type), typeLabels, GUILayout.MinWidth(120));
            int newRank = EditorGUILayout.Popup(Mathf.Max(0, rank), RankLabels(types[newType]), GUILayout.Width(80));
            if (newType != type || newRank != rank) variantProp.objectReferenceValue = Pick(types[newType], newRank);

            countProp.intValue = Mathf.Max(1, EditorGUILayout.IntField(Mathf.Max(1, countProp.intValue), GUILayout.Width(45)));
            bool lead = EditorGUILayout.Toggle(leaderProp.boolValue, GUILayout.Width(34));
            if (lead && !leaderProp.boolValue) ClearLeaders(members);
            leaderProp.boolValue = lead;
            if (GUILayout.Button("X", GUILayout.Width(22))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) members.DeleteArrayElementAtIndex(remove);

        if (GUILayout.Button("Add Member"))
        {
            members.InsertArrayElementAtIndex(members.arraySize);
            var m = members.GetArrayElementAtIndex(members.arraySize - 1);
            m.FindPropertyRelative("variant").objectReferenceValue = Pick(types[0], 0);
            m.FindPropertyRelative("count").intValue = 1;
            m.FindPropertyRelative("leader").boolValue = false;
        }

        var spread = so.FindProperty("spread");
        spread.floatValue = EditorGUILayout.Slider("Spread (m)", spread.floatValue, 0f, 15f);
        so.ApplyModifiedProperties();

        EditorGUILayout.LabelField($"Total: {current.TotalCount}   Leader: {LeaderText(current)}", EditorStyles.miniLabel);
    }

    private static void ClearLeaders(SerializedProperty members)
    {
        for (int i = 0; i < members.arraySize; i++)
            members.GetArrayElementAtIndex(i).FindPropertyRelative("leader").boolValue = false;
    }

    private static string LeaderText(EnemySquadSO s)
    {
        foreach (var m in s.members) if (m.leader && m.variant != null) return m.variant.name;
        return "highest rank";
    }

    // ---- enemy types ----

    private void BuildTypes()
    {
        types.Clear();
        var byKey = new Dictionary<string, EnemyType>();
        var all = AssetDatabase.FindAssets("t:EnemyVariantSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<EnemyVariantSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(v => v != null && v.archetype != null).OrderBy(v => v.name);
        foreach (var v in all)
        {
            int rank = RankOf(v);
            if (rank < 0 || rank > 3) continue;
            string key = TypeLabel(v);
            if (!byKey.TryGetValue(key, out var t)) { t = new EnemyType { Label = key }; byKey[key] = t; types.Add(t); }
            if (t.ByRank[rank] == null) t.ByRank[rank] = v;
        }
        types.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        typeLabels = types.Select(t => t.Label).ToArray();
    }

    private void Locate(EnemyVariantSO v, out int type, out int rank)
    {
        type = rank = -1;
        if (v == null) return;
        for (int t = 0; t < types.Count; t++)
            for (int r = 0; r < 4; r++)
                if (types[t].ByRank[r] == v) { type = t; rank = r; return; }
    }

    // The variant at this rank, or the nearest rank the type has
    private static EnemyVariantSO Pick(EnemyType t, int rank)
    {
        if (t.ByRank[rank] != null) return t.ByRank[rank];
        for (int d = 1; d < 4; d++)
        {
            if (rank - d >= 0 && t.ByRank[rank - d] != null) return t.ByRank[rank - d];
            if (rank + d < 4 && t.ByRank[rank + d] != null) return t.ByRank[rank + d];
        }
        return null;
    }

    private static string[] RankLabels(EnemyType t)
        => Enumerable.Range(0, 4).Select(r => t.ByRank[r] != null ? RankNames[r] : RankNames[r] + " (none)").ToArray();

    private static int RankOf(EnemyVariantSO v)
    {
        int rank = (int)v.archetype.rank;
        foreach (var m in v.modifiers)
            if (m != null && (int)m.raiseRankTo > rank) rank = (int)m.raiseRankTo;
        return rank;
    }

    // Archetype name with every non-tier prefix, e.g. "Volatile Alien Zombie"
    private static string TypeLabel(EnemyVariantSO v)
    {
        var sb = new StringBuilder();
        foreach (var m in v.modifiers)
            if (m != null && m.kind != EnemyModifierKind.Tier && !string.IsNullOrEmpty(m.namePrefix))
                sb.Append(m.namePrefix).Append(' ');
        sb.Append(v.archetype.StatSourceLabel);
        return sb.ToString();
    }

    // ---- spawn ----

    private void DrawSpawn()
    {
        EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
        if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play to spawn the squad.", MessageType.None); return; }
        using (new EditorGUI.DisabledScope(current == null || current.TotalCount == 0))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn In Front Of Player")) Spawn(InFrontOfPlayer());
            if (GUILayout.Button("Spawn At Scene View")) Spawn(AtSceneView());
            EditorGUILayout.EndHorizontal();
        }
    }

    private void Spawn(Vector3? at)
    {
        if (at == null) { Debug.LogWarning("[EnemySquadSetup] No spawn point (no player or Scene view)."); return; }
        var spawner = FindFirstObjectByType<EnemySpawner>();
        if (spawner == null) { Debug.LogError("[EnemySquadSetup] No EnemySpawner in the scene."); return; }
        var list = spawner.SpawnSquad(current, at.Value);
        Debug.Log($"[EnemySquadSetup] Spawned {list.Count}x {current.Label}.");
    }

    private static Vector3? InFrontOfPlayer()
    {
        var player = AITargets.Player;
        var cam = Camera.main;
        if (player == null || cam == null) return null;
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = player.forward;
        Vector3 p = AITargets.FeetOf(player) + fwd.normalized * PlayerSpawnDistance;
        return OnNavMesh(p);
    }

    private static Vector3? AtSceneView()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) return null;
        var cam = view.camera.transform;
        Vector3 p = Physics.Raycast(cam.position, cam.forward, out var hit, 300f) ? hit.point : view.pivot;
        return OnNavMesh(p);
    }

    private static Vector3 OnNavMesh(Vector3 p) => NavMesh.SamplePosition(p, out var hit, 5f, NavMesh.AllAreas) ? hit.position : p;

    // ---- scene groups ----

    private void DrawScene()
    {
        EditorGUILayout.LabelField("Scene enemies", EditorStyles.boldLabel);
        var selected = SelectedEnemies();
        using (new EditorGUI.DisabledScope(Application.isPlaying || selected.Count < 2))
            if (GUILayout.Button($"Group Selected Into Squad ({selected.Count} enemies)")) GroupSelected(selected);
        using (new EditorGUI.DisabledScope(Application.isPlaying || SelectedGroups().Count == 0))
            if (GUILayout.Button("Ungroup Selected Squad")) UngroupSelected();
    }

    private static List<EnemyBrain> SelectedEnemies()
    {
        var list = new List<EnemyBrain>();
        foreach (var go in Selection.gameObjects)
        {
            var b = go.GetComponentInParent<EnemyBrain>();
            if (b != null && !list.Contains(b)) list.Add(b);
        }
        return list;
    }

    private static List<EnemySquadGroup> SelectedGroups()
        => Selection.gameObjects.Select(g => g.GetComponent<EnemySquadGroup>()).Where(g => g != null).ToList();

    private static void GroupSelected(List<EnemyBrain> enemies)
    {
        Vector3 c = Vector3.zero;
        foreach (var b in enemies) c += b.transform.position;
        c /= enemies.Count;

        var go = new GameObject("Squad");
        Undo.RegisterCreatedObjectUndo(go, "Group Squad");
        go.transform.position = c;
        Undo.AddComponent<EnemySquadGroup>(go);
        foreach (var b in enemies) Undo.SetTransformParent(b.transform, go.transform, "Group Squad");
        Selection.activeGameObject = go;
    }

    private static void UngroupSelected()
    {
        foreach (var g in SelectedGroups())
        {
            var parent = g.transform.parent;
            foreach (var b in g.GetComponentsInChildren<EnemyBrain>(true))
                Undo.SetTransformParent(b.transform, parent, "Ungroup Squad");
            Undo.DestroyObjectImmediate(g.gameObject);
        }
    }

    // ---- live ----

    private void DrawLive()
    {
        EditorGUILayout.LabelField("Live squads", EditorStyles.boldLabel);
        if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play to see squads, roles and leaders.", MessageType.None); return; }
        if (EnemySquad.All.Count == 0) { EditorGUILayout.HelpBox("No squads alive.", MessageType.None); return; }

        foreach (var squad in EnemySquad.All)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{squad.Label}  ({squad.Members.Count}){(squad.LeaderLost ? "  leader lost" : "")}", EditorStyles.miniBoldLabel);
            foreach (var b in squad.Members)
            {
                if (b == null) continue;
                EditorGUILayout.BeginHorizontal();
                string side = b.Role == SquadRole.Flanker ? (b.FlankSide > 0 ? " R" : " L") : "";
                EditorGUILayout.LabelField($"{b.Role}{side}", GUILayout.Width(90));
                EditorGUILayout.LabelField($"{b.Rank}  {b.name}");
                if (GUILayout.Button("Select", GUILayout.Width(60))) { Selection.activeGameObject = b.gameObject; SceneView.FrameLastActiveSceneView(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }
    }
}
