using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using UnityEngine.AI;
using Combat.Enemies;

// Opens the enemy gallery scene, building it with neutral studio lighting the first time.
public static class EnemyGallerySceneBuilder
{
    private const string ScenePath = "Assets/Scenes/EnemyGallery.unity";
    private const string FloorPath = "Assets/Scenes/EnemyGallery_Floor.mat";
    private const string ManagerScenePath = "Assets/Scenes/EncounterTest.unity";
    private static readonly string[] Managers = { "StatsManager", "StatusManager", "DamageNumberPool", "DamageAccumulatorRegistry" };

    [MenuItem("FPS0/Enemies/Gallery Scene", priority = 1)]
    private static void Open()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            EditorSceneManager.OpenScene(ScenePath);
            return;
        }
        Build();
    }

    private static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.43f, 0.46f);
        RenderSettings.ambientEquatorColor = new Color(0.3f, 0.3f, 0.32f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.16f, 0.17f);
        RenderSettings.fog = false;

        Light("Key Light", 1.25f, new Vector3(40f, 150f, 0f), new Color(1f, 0.97f, 0.92f), true);
        Light("Fill Light", 0.45f, new Vector3(20f, 230f, 0f), new Color(0.85f, 0.9f, 1f), false);
        Light("Rim Light", 0.8f, new Vector3(15f, 20f, 0f), Color.white, false);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(6f, 1f, 6f);
        floor.transform.position = new Vector3(0f, 0f, 10f);
        floor.GetComponent<Renderer>().sharedMaterial = FloorMaterial();

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.21f, 0.23f);
        cam.fieldOfView = 40f;
        camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -6f), Quaternion.Euler(8f, 0f, 0f));

        new GameObject("Enemy Gallery").AddComponent<EnemyGallery>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        SetupEffects();
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(0f, 1f, 2f), Quaternion.Euler(12f, 0f, 0f), 7f);
    }

    // Managers the effects lineup needs (copied from the encounter scene) and a walkable floor for knockback. Safe to run again.
    public static void SetupEffects()
    {
        var gallery = SceneManager.GetActiveScene();
        if (gallery.path != ScenePath) { Debug.LogWarning("[EnemyGallery] Open the gallery scene first (FPS0/Enemies/Gallery Scene)."); return; }

        var missing = new List<string>();
        foreach (var n in Managers)
            if (GameObject.Find(n) == null) missing.Add(n);
        if (missing.Count > 0)
        {
            var src = EditorSceneManager.OpenScene(ManagerScenePath, OpenSceneMode.Additive);
            foreach (var root in src.GetRootGameObjects())
            {
                if (!missing.Contains(root.name)) continue;
                var copy = Object.Instantiate(root);
                copy.name = root.name;
                SceneManager.MoveGameObjectToScene(copy, gallery);
                missing.Remove(root.name);
                Debug.Log("[EnemyGallery] Added " + root.name);
            }
            EditorSceneManager.CloseScene(src, true);
            SceneManager.SetActiveScene(gallery);
            foreach (var n in missing) Debug.LogWarning("[EnemyGallery] No " + n + " in " + ManagerScenePath);
        }

        var floor = GameObject.Find("Floor");
        if (floor != null && floor.GetComponent<NavMeshSurface>() == null)
        {
            var nav = floor.AddComponent<NavMeshSurface>();
            nav.collectObjects = CollectObjects.Children;
            foreach (var guid in AssetDatabase.FindAssets("t:EnemyArchetypeSO"))
            {
                var arch = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                var agent = arch != null && arch.bodyPrefab != null ? arch.bodyPrefab.GetComponent<NavMeshAgent>() : null;
                if (agent != null) { nav.agentTypeID = agent.agentTypeID; break; }
            }
            Debug.Log("[EnemyGallery] NavMeshSurface on Floor (built at Play)");
        }

        EditorSceneManager.MarkSceneDirty(gallery);
        EditorSceneManager.SaveScene(gallery);
    }

    private static void Light(string name, float intensity, Vector3 euler, Color color, bool shadows)
    {
        var go = new GameObject(name);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = intensity;
        l.color = color;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        go.transform.rotation = Quaternion.Euler(euler);
    }

    private static Material FloorMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FloorPath);
        if (mat != null) return mat;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader);
        mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.47f));
        mat.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(mat, FloorPath);
        return mat;
    }
}
