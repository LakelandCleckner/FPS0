using UnityEditor;
using UnityEngine;
using Combat.Weapons;

// Poses the body, puts a gun prefab in its right hand, and saves the third-person grip points onto the prefab
public class TPGripTool : EditorWindow
{
    private const string GripName = "TP_Grip";
    private const string SupportName = "TP_Support";
    private const string MagWellName = "TP_MagWell";
    private const string LoadPortName = "TP_LoadPort";

    [SerializeField] private Animator body;
    [SerializeField] private GameObject gunPrefab;
    [SerializeField] private AnimationClip pose;
    [SerializeField] private float poseTime;

    private GameObject gun;
    private Transform support, magWell, loadPort;
    private bool previewing;

    [MenuItem("Tools/TP Grip Tool")]
    private static void Open() => GetWindow<TPGripTool>("TP Grip Tool");

    private void OnEnable()
    {
        if (pose == null)
        {
            var guids = AssetDatabase.FindAssets("TP_Hold_Aim t:AnimationClip");
            if (guids.Length > 0) pose = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
        if (body == null)
        {
            var tpw = FindFirstObjectByType<ThirdPersonWeapon>();
            if (tpw != null) body = tpw.GetComponent<Animator>();
        }
    }

    private void OnDisable() => StopPreview();

    private void OnGUI()
    {
        using (new EditorGUI.DisabledScope(previewing))
        {
            body = (Animator)EditorGUILayout.ObjectField("Body (scene)", body, typeof(Animator), true);
            gunPrefab = (GameObject)EditorGUILayout.ObjectField("Gun Prefab", gunPrefab, typeof(GameObject), false);
            pose = (AnimationClip)EditorGUILayout.ObjectField("Pose Clip", pose, typeof(AnimationClip), false);
        }

        if (pose != null)
        {
            EditorGUI.BeginChangeCheck();
            poseTime = EditorGUILayout.Slider("Pose Time", poseTime, 0f, pose.length);
            if (EditorGUI.EndChangeCheck() && previewing) Sample();
        }

        EditorGUILayout.Space();

        if (!previewing)
        {
            string problem = Problem();
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Info);
            using (new EditorGUI.DisabledScope(problem != null))
                if (GUILayout.Button("Start Preview", GUILayout.Height(28))) StartPreview();
            return;
        }

        EditorGUILayout.HelpBox("1. Move/rotate/scale the gun until it sits in the right hand, " +
                                "barrel pointing straight along the body's forward (blue axis).\n" +
                                "2. Move TP_Support onto the foregrip (rotate it to match the left hand).\n" +
                                "3. Move TP_MagWell onto the mag well and TP_LoadPort onto the shell port.\n" +
                                "4. Save to Prefab.", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Gun")) Selection.activeTransform = gun.transform;
        if (GUILayout.Button("Support")) Selection.activeTransform = support;
        if (GUILayout.Button("Mag Well")) Selection.activeTransform = magWell;
        if (GUILayout.Button("Load Port")) Selection.activeTransform = loadPort;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save to Prefab", GUILayout.Height(28))) Save();
        if (GUILayout.Button("Stop Preview", GUILayout.Height(28))) StopPreview();
        EditorGUILayout.EndHorizontal();
    }

    private string Problem()
    {
        if (body == null) return "Assign the body (the Ch15 in the scene).";
        if (!body.isHuman) return "Body Animator needs a humanoid avatar.";
        if (gunPrefab == null) return "Assign a gun prefab.";
        if (gunPrefab.GetComponent<GunModel>() == null) return "Gun prefab needs a GunModel on its root.";
        if (pose == null) return "Assign a pose clip (TP_Hold_Aim).";
        return null;
    }

    // ===== Preview =====

    private void StartPreview()
    {
        AnimationMode.StartAnimationMode();
        Sample();

        var hand = body.GetBoneTransform(HumanBodyBones.RightHand);
        gun = Instantiate(gunPrefab);
        gun.name = "TP_Gun (preview)";
        gun.hideFlags = HideFlags.DontSave;

        var gm = gun.GetComponent<GunModel>();
        ThirdPersonWeapon.Attach(gun.transform, gm, hand, gunPrefab.transform.localScale);
        gun.transform.SetParent(null, true);

        var left = body.GetBoneTransform(HumanBodyBones.LeftHand);
        support = Marker(gm.TpSupport, SupportName, left.position, left.rotation);
        magWell = Marker(gm.TpMagWell, MagWellName, gun.transform.position - gun.transform.up * 0.08f, gun.transform.rotation);
        loadPort = Marker(gm.TpLoadPort, LoadPortName, gun.transform.position, gun.transform.rotation);
        foreach (var t in gun.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;

        previewing = true;
        SceneView.duringSceneGui += DrawMarkers;
        Selection.activeTransform = gun.transform;
        SceneView.RepaintAll();
    }

    private void StopPreview()
    {
        SceneView.duringSceneGui -= DrawMarkers;
        if (gun != null) DestroyImmediate(gun);
        gun = null;
        support = magWell = loadPort = null;
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        previewing = false;
        SceneView.RepaintAll();
    }

    private void Sample()
    {
        if (body == null || pose == null) return;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(body.gameObject, pose, poseTime);
        AnimationMode.EndSampling();
        SceneView.RepaintAll();
    }

    // Existing point on the preview copy, or a new one at the default pose
    private Transform Marker(Transform existing, string name, Vector3 pos, Quaternion rot)
    {
        if (existing != null) return existing;
        var t = new GameObject(name).transform;
        t.SetParent(gun.transform, false);
        t.SetPositionAndRotation(pos, rot);
        return t;
    }

    private void DrawMarkers(SceneView view)
    {
        if (gun == null) return;
        DrawMarker(support, new Color(0.3f, 0.8f, 1f));
        DrawMarker(magWell, new Color(1f, 0.8f, 0.2f));
        DrawMarker(loadPort, new Color(1f, 0.4f, 0.3f));
    }

    private static void DrawMarker(Transform t, Color c)
    {
        if (t == null) return;
        Handles.color = c;
        float size = HandleUtility.GetHandleSize(t.position) * 0.08f;
        Handles.SphereHandleCap(0, t.position, t.rotation, size, EventType.Repaint);
        Handles.Label(t.position + Vector3.up * size, t.name);
    }

    // ===== Save =====

    // Grip = the hand's pose in the gun's space; markers keep their pose in the gun's space
    private void Save()
    {
        var hand = body.GetBoneTransform(HumanBodyBones.RightHand);
        var g = gun.transform;
        float baseScale = Mathf.Abs(gunPrefab.transform.localScale.x);
        float tpScale = baseScale > 0f ? Mathf.Abs(g.lossyScale.x) / baseScale : 1f;

        string path = AssetDatabase.GetAssetPath(gunPrefab);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var gm = root.GetComponent<GunModel>();
            var grip = Point(root.transform, GripName);
            Copy(g, hand.position, hand.rotation, grip);
            var sup = Point(root.transform, SupportName);
            Copy(g, support.position, support.rotation, sup);
            var mag = Point(root.transform, MagWellName);
            Copy(g, magWell.position, magWell.rotation, mag);
            var port = Point(root.transform, LoadPortName);
            Copy(g, loadPort.position, loadPort.rotation, port);

            var so = new SerializedObject(gm);
            so.FindProperty("tpGrip").objectReferenceValue = grip;
            so.FindProperty("tpSupport").objectReferenceValue = sup;
            so.FindProperty("tpMagWell").objectReferenceValue = mag;
            so.FindProperty("tpLoadPort").objectReferenceValue = port;
            so.FindProperty("tpScale").floatValue = tpScale;
            so.FindProperty("tpAimAxis").vector3Value = (Quaternion.Inverse(g.rotation) * body.transform.forward).normalized;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Debug.Log($"[TPGripTool] Saved grip points to '{path}' (scale {tpScale:F3}).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Transform Point(Transform root, string name)
    {
        var t = root.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    // World pose measured on the preview gun, written as the same local pose under the prefab root
    private static void Copy(Transform previewRoot, Vector3 worldPos, Quaternion worldRot, Transform target)
    {
        target.localPosition = previewRoot.InverseTransformPoint(worldPos);
        target.localRotation = Quaternion.Inverse(previewRoot.rotation) * worldRot;
        target.localScale = Vector3.one;
    }
}
