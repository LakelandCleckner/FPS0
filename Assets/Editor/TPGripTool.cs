using UnityEditor;
using UnityEngine;
using Combat.Weapons;

// Poses the body, puts a gun prefab in its right hand, previews the left hand live,
// and saves the third-person grip points onto the prefab
public class TPGripTool : EditorWindow
{
    private const string GripName = "TP_Grip";
    private const string SupportName = "TP_Support";
    private const string MagWellName = "TP_MagWell";
    private const string LoadPortName = "TP_LoadPort";

    private enum HandPreview { Support, MagWell, LoadPort, Belt, MagReload, ShellLoad, Off }

    [SerializeField] private Animator body;
    [SerializeField] private GameObject gunPrefab;
    [SerializeField] private AnimationClip pose;
    [SerializeField] private float poseTime;
    [SerializeField] private HandPreview handPreview = HandPreview.Support;
    [SerializeField] private float loopSeconds = 2f;
    [SerializeField] private bool useSupportRotation;

    private GameObject gun;
    private Transform support, magWell, loadPort, belt;
    private Transform leftUpper, leftLower, leftHand, hips;
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

        if (pose != null) poseTime = EditorGUILayout.Slider("Pose Time", poseTime, 0f, pose.length);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Left hand preview", EditorStyles.boldLabel);
        handPreview = (HandPreview)EditorGUILayout.EnumPopup("Hand At", handPreview);
        if (handPreview == HandPreview.MagReload || handPreview == HandPreview.ShellLoad)
            loopSeconds = Mathf.Max(0.2f, EditorGUILayout.FloatField("Loop Seconds", loopSeconds));
        useSupportRotation = EditorGUILayout.Toggle(new GUIContent("Use Support Rotation",
            "Preview with Support Rotation Weight = 1. Off matches the default (wrist keeps the pose's rotation)."), useSupportRotation);

        EditorGUILayout.Space();

        if (!previewing)
        {
            string problem = Problem();
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Info);
            using (new EditorGUI.DisabledScope(problem != null))
                if (GUILayout.Button("Start Preview", GUILayout.Height(28))) StartPreview();
            return;
        }

        EditorGUILayout.HelpBox("1. Move/rotate/scale the gun into the right hand; line the barrel up with the yellow line.\n" +
                                "2. Move TP_Support onto the foregrip and watch the left hand land.\n" +
                                "3. Hand At: Mag Well / Load Port / Belt to check each reach; Mag Reload / Shell Load to loop the path.\n" +
                                "4. Save to Prefab.", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Gun")) Selection.activeTransform = gun.transform;
        if (GUILayout.Button("Support")) Selection.activeTransform = support;
        if (GUILayout.Button("Mag Well")) Selection.activeTransform = magWell;
        if (GUILayout.Button("Load Port")) Selection.activeTransform = loadPort;
        using (new EditorGUI.DisabledScope(belt == null))
            if (GUILayout.Button("Belt")) Selection.activeTransform = belt;
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Reset Support Rotation")) ResetSupportRotation();

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

        leftUpper = body.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLower = body.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = body.GetBoneTransform(HumanBodyBones.LeftHand);
        hips = body.GetBoneTransform(HumanBodyBones.Hips);
        belt = null;
        foreach (var t in body.GetComponentsInChildren<Transform>(true))
            if (t.name == "TP_Belt") { belt = t; break; }

        var hand = body.GetBoneTransform(HumanBodyBones.RightHand);
        gun = Instantiate(gunPrefab);
        gun.name = "TP_Gun (preview)";

        var gm = gun.GetComponent<GunModel>();
        ThirdPersonWeapon.Attach(gun.transform, gm, hand, gunPrefab.transform.localScale);
        gun.transform.SetParent(null, true);

        support = Marker(gm.TpSupport, SupportName, leftHand.position, leftHand.rotation);
        magWell = Marker(gm.TpMagWell, MagWellName, gun.transform.position - gun.transform.up * 0.08f, gun.transform.rotation);
        loadPort = Marker(gm.TpLoadPort, LoadPortName, gun.transform.position, gun.transform.rotation);
        foreach (var t in gun.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;

        previewing = true;
        SceneView.duringSceneGui += DrawMarkers;
        EditorApplication.update += Tick;
        Selection.activeTransform = gun.transform;
    }

    private void StopPreview()
    {
        EditorApplication.update -= Tick;
        SceneView.duringSceneGui -= DrawMarkers;
        if (gun != null) DestroyImmediate(gun);
        gun = null;
        support = magWell = loadPort = belt = null;
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        previewing = false;
        SceneView.RepaintAll();
    }

    // Re-pose every editor tick, then put the left hand where the preview mode says
    private void Tick()
    {
        if (!previewing || body == null || gun == null) { StopPreview(); return; }

        Sample();
        if (handPreview != HandPreview.Off && support != null)
        {
            Vector3 target = HandTarget();
            float rotWeight = useSupportRotation ? 1f : 0f;
            ThirdPersonWeapon.SolveTwoBone(leftUpper, leftLower, leftHand, target, support.rotation, 1f, rotWeight);
        }
        SceneView.RepaintAll();
    }

    private Vector3 HandTarget()
    {
        Vector3 sup = support.position;
        Vector3 mag = magWell != null ? magWell.position : sup;
        Vector3 port = loadPort != null ? loadPort.position : mag;
        Vector3 pouch = ThirdPersonWeapon.BeltPosition(belt, hips, body.transform);
        float loop = (float)(EditorApplication.timeSinceStartup % loopSeconds) / loopSeconds;

        switch (handPreview)
        {
            case HandPreview.MagWell: return mag;
            case HandPreview.LoadPort: return port;
            case HandPreview.Belt: return pouch;
            case HandPreview.MagReload: return ThirdPersonWeapon.MagPath(sup, mag, pouch, loop);
            case HandPreview.ShellLoad: return ThirdPersonWeapon.ShellPath(pouch, port, loop);
            default: return sup;
        }
    }

    private void Sample()
    {
        if (body == null || pose == null) return;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(body.gameObject, pose, poseTime);
        AnimationMode.EndSampling();
    }

    // Support marker back to the pose's own left hand rotation
    private void ResetSupportRotation()
    {
        if (support == null) return;
        Sample();
        Undo.RecordObject(support, "Reset Support Rotation");
        support.rotation = leftHand.rotation;
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
        if (gun == null || body == null) return;
        DrawMarker(support, new Color(0.3f, 0.8f, 1f));
        DrawMarker(magWell, new Color(1f, 0.8f, 0.2f));
        DrawMarker(loadPort, new Color(1f, 0.4f, 0.3f));
        DrawMarker(belt, new Color(0.6f, 1f, 0.4f));

        // Barrel guide: the body's forward from the gun
        Handles.color = Color.yellow;
        Vector3 from = gun.transform.position;
        Handles.DrawLine(from, from + body.transform.forward * 1.5f, 2f);
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
        Sample();
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
