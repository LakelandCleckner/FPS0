using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Opens the enemy gallery scene, building it with neutral studio lighting the first time.
public static class EnemyGallerySceneBuilder
{
    private const string ScenePath = "Assets/Scenes/EnemyGallery.unity";
    private const string FloorPath = "Assets/Scenes/EnemyGallery_Floor.mat";

    [MenuItem("Tools/Enemy Gallery")]
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
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(0f, 1f, 2f), Quaternion.Euler(12f, 0f, 0f), 7f);
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
