using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Combat.AI;
using Combat.Encounters;
using Combat.Spawning;

// Generate procedural encounter rooms, edit an encounter's waves, check the scene has what encounters need,
// and watch a running encounter.
public class EncounterSetup : EditorWindow
{
    private const string SettingsFolder = "Assets/GameData/Encounters";
    private const string AmmoPrefabPath = "Assets/Prefabs/Weapons/PrimaryAmmoBoxRoot.prefab";

    private EncounterRoomSettingsSO settings;
    private int seed = 1;
    private Vector3 origin = new Vector3(60f, 0f, 0f);
    private Vector2 scroll;

    // Scene check results, refreshed every few seconds outside Play (the lookups are slow)
    private const double CheckInterval = 3.0;
    private double nextCheck;
    private bool hasPlayer, hasSpawner, hasGoap, hasSurface, hasSquads;

    [MenuItem("Tools/Encounter Setup")]
    private static void Open() => GetWindow<EncounterSetup>("Encounters");

    private void OnEnable()
    {
        if (settings == null)
            settings = AssetDatabase.FindAssets("t:EncounterRoomSettingsSO")
                .Select(g => AssetDatabase.LoadAssetAtPath<EncounterRoomSettingsSO>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(x => x != null);
    }

    // In Play, only redraw while showing a live encounter
    private void OnInspectorUpdate()
    {
        if (Application.isPlaying && Selection.activeGameObject != null && Selection.activeGameObject.GetComponentInParent<Encounter>() != null)
            Repaint();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawSceneCheck();
        EditorGUILayout.Space();
        DrawGenerate();
        EditorGUILayout.Space();
        DrawSelected();
        EditorGUILayout.EndScrollView();
    }

    // ---- scene check ----

    private void DrawSceneCheck()
    {
        EditorGUILayout.LabelField("Scene check", EditorStyles.boldLabel);
        if (!Application.isPlaying && EditorApplication.timeSinceStartup >= nextCheck) RunSceneCheck();
        Row("Player (tag Player)", hasPlayer);
        Row("EnemySpawner", hasSpawner);
        Row("GOAP manager", hasGoap);
        Row("NavMesh surface (optional, rooms add one)", hasSurface, true);
        Row("Squad makeups", hasSquads);
    }

    private void RunSceneCheck()
    {
        nextCheck = EditorApplication.timeSinceStartup + CheckInterval;
        hasPlayer = GameObject.FindWithTag("Player") != null;
        hasSpawner = FindFirstObjectByType<EnemySpawner>() != null;
        hasGoap = FindFirstObjectByType<CrashKonijn.Goap.Runtime.GoapBehaviour>() != null;
        hasSurface = FindFirstObjectByType<NavMeshSurface>() != null;
        hasSquads = AssetDatabase.FindAssets("t:EnemySquadSO").Length > 0;
    }

    private static void Row(string label, bool ok, bool optional = false)
    {
        var style = new GUIStyle(EditorStyles.label);
        style.normal.textColor = ok ? new Color(0.4f, 0.85f, 0.4f) : optional ? new Color(0.9f, 0.8f, 0.3f) : new Color(1f, 0.45f, 0.4f);
        EditorGUILayout.LabelField((ok ? "OK      " : optional ? "None    " : "MISSING ") + label, style);
    }

    // ---- generate ----

    private void DrawGenerate()
    {
        EditorGUILayout.LabelField("Generate room", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        settings = (EncounterRoomSettingsSO)EditorGUILayout.ObjectField("Settings", settings, typeof(EncounterRoomSettingsSO), false);
        if (GUILayout.Button("New", GUILayout.Width(50))) settings = NewSettings();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        seed = EditorGUILayout.IntField("Seed", seed);
        if (GUILayout.Button("Random", GUILayout.Width(70))) seed = Random.Range(1, 1000000);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        origin = EditorGUILayout.Vector3Field("Origin", origin);
        if (GUILayout.Button("Scene View", GUILayout.Width(80)) && SceneView.lastActiveSceneView != null)
        {
            Vector3 p = SceneView.lastActiveSceneView.pivot;
            origin = new Vector3(Mathf.Round(p.x), 0f, Mathf.Round(p.z));
        }
        EditorGUILayout.EndHorizontal();

        if (settings != null && settings.squadPool.Count == 0)
            EditorGUILayout.HelpBox("Settings have no squads in the pool: rooms get no waves.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(settings == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate")) Generate(seed, origin);
            var room = SelectedRoom();
            using (new EditorGUI.DisabledScope(room == null))
            {
                if (GUILayout.Button("Reroll Selected")) Reroll(room);
                if (GUILayout.Button("Delete Selected")) Delete(room);
            }
            EditorGUILayout.EndHorizontal();
        }
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("The NavMesh is rebuilt with the room's walls and cover when Play starts.", MessageType.None);
    }

    private void Generate(int roomSeed, Vector3 at)
    {
        var root = EncounterRoomGenerator.Generate(settings, roomSeed, at);
        if (Application.isPlaying) return;   // the room rebuilds the NavMesh on Start
        Undo.RegisterCreatedObjectUndo(root, "Generate Encounter Room");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
    }

    private void Reroll(EncounterRoom room)
    {
        Vector3 at = room.transform.position;
        if (room.settings != null) settings = room.settings;
        seed = Random.Range(1, 1000000);
        Delete(room);
        Generate(seed, at);
    }

    private static void Delete(EncounterRoom room)
    {
        if (Application.isPlaying) Destroy(room.gameObject);
        else Undo.DestroyObjectImmediate(room.gameObject);
    }

    private static EncounterRoom SelectedRoom()
        => Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<EncounterRoom>() : null;

    // Prefilled: every squad makeup in the pool, the ammo box
    private static EncounterRoomSettingsSO NewSettings()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolder)) AssetDatabase.CreateFolder("Assets/GameData", "Encounters");
        var s = CreateInstance<EncounterRoomSettingsSO>();
        s.squadPool = AssetDatabase.FindAssets("t:EnemySquadSO")
            .Select(g => AssetDatabase.LoadAssetAtPath<EnemySquadSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(q => q != null).ToList();
        s.ammoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AmmoPrefabPath);
        string path = AssetDatabase.GenerateUniqueAssetPath(SettingsFolder + "/RoomSettings.asset");
        AssetDatabase.CreateAsset(s, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(s);
        return s;
    }

    // ---- selected encounter ----

    private void DrawSelected()
    {
        EditorGUILayout.LabelField("Selected encounter", EditorStyles.boldLabel);
        var enc = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<Encounter>() : null;
        if (enc == null) { EditorGUILayout.HelpBox("Select a room or an Encounter to edit its waves.", MessageType.None); return; }

        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField($"{enc.Current}   wave {enc.WaveIndex + 1}/{enc.WaveCount}   enemies left {enc.Alive}");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start")) enc.Begin();
            if (GUILayout.Button("Reset")) enc.ResetEncounter();
            EditorGUILayout.EndHorizontal();
        }

        var so = new SerializedObject(enc);
        EditorGUILayout.PropertyField(so.FindProperty("startOnEnter"));
        EditorGUILayout.PropertyField(so.FindProperty("waves"), true);
        EditorGUILayout.PropertyField(so.FindProperty("doors"), true);
        so.ApplyModifiedProperties();

        if (GUILayout.Button("Regenerate Waves From Settings"))
        {
            var room = enc.GetComponent<EncounterRoom>();
            var s = room != null && room.settings != null ? room.settings : settings;
            if (s != null)
            {
                Undo.RecordObject(enc, "Regenerate Waves");
                var waves = EncounterRoomGenerator.GenerateWaves(s, new System.Random(Random.Range(1, 1000000)), enc.Doors.Count);
                enc.Waves.Clear();
                enc.Waves.AddRange(waves);
                EditorUtility.SetDirty(enc);
            }
        }
    }
}
