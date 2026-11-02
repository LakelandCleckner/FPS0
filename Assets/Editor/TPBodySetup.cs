using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds the TP_Body controller (base + upper body layers) and wires the body onto the player
public class TPBodySetup : EditorWindow
{
    private const string TreePrefix = "TPB_";
    private const string UpperLayer = "UpperBody";
    private const string GeneratedFolder = "TP_Generated";
    private const string DefaultClipFolder = "Assets/Animations/Player/ThirdPerson Weapon Packs/Rifle Locomotion";
    private const string DefaultPistolFolder = "Assets/Animations/Player/ThirdPerson Weapon Packs/Pistol";

    // Upper body hold poses: generated clip name and source clip
    private const string HoldHip = "TP_Hold_Hip";
    private const string HoldAim = "TP_Hold_Aim";
    private const string HoldHipSource = "idle";
    private const string HoldAimSource = "idle aiming";
    private const string PistolHoldSource = "pistol idle";

    // Clip file name and blend position (1 = walk, 2 = sprint)
    private static readonly (string clip, float x, float y)[] Stand =
    {
        ("idle", 0f, 0f),
        ("walk forward", 0f, 1f),
        ("walk forward left", -0.7f, 0.7f),
        ("walk forward right", 0.7f, 0.7f),
        ("walk left", -1f, 0f),
        ("walk right", 1f, 0f),
        ("walk backward", 0f, -1f),
        ("walk backward left", -0.7f, -0.7f),
        ("walk backward right", 0.7f, -0.7f),
        ("sprint forward", 0f, 2f),
        ("sprint forward left", -1.4f, 1.4f),
        ("sprint forward right", 1.4f, 1.4f),
    };

    private static readonly (string clip, float x, float y)[] Crouch =
    {
        ("idle crouching", 0f, 0f),
        ("walk crouching forward", 0f, 1f),
        ("walk crouching forward left", -0.7f, 0.7f),
        ("walk crouching forward right", 0.7f, 0.7f),
        ("walk crouching left", -1f, 0f),
        ("walk crouching right", 1f, 0f),
        ("walk crouching backward", 0f, -1f),
        ("walk crouching backward left", -0.7f, -0.7f),
        ("walk crouching backward right", 0.7f, -0.7f),
    };

    [SerializeField] private DefaultAsset clipFolder;
    [SerializeField] private DefaultAsset pistolFolder;
    [SerializeField] private AnimatorController controller;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private GameObject body;
    [SerializeField] private float backOffset = -0.2f;
    [SerializeField] private float transitionTime = 0.1f;
    [SerializeField] private float jumpUpExitTime = 0.8f;
    [SerializeField] private float jumpDownExitTime = 0.5f;

    [MenuItem("Tools/TP Body Setup")]
    private static void Open() => GetWindow<TPBodySetup>("TP Body Setup");

    private void OnEnable()
    {
        if (clipFolder == null) clipFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(DefaultClipFolder);
        if (pistolFolder == null) pistolFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(DefaultPistolFolder);
        if (playerRoot == null)
        {
            var pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) playerRoot = pm.transform;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Controller", EditorStyles.boldLabel);
        clipFolder = (DefaultAsset)EditorGUILayout.ObjectField("Clip Folder", clipFolder, typeof(DefaultAsset), false);
        pistolFolder = (DefaultAsset)EditorGUILayout.ObjectField("Pistol Clip Folder", pistolFolder, typeof(DefaultAsset), false);
        controller = (AnimatorController)EditorGUILayout.ObjectField("Controller (empty = create)", controller, typeof(AnimatorController), false);
        transitionTime = EditorGUILayout.FloatField("Transition Time (s)", transitionTime);
        jumpUpExitTime = EditorGUILayout.FloatField("Jump Up Exit Time", jumpUpExitTime);
        jumpDownExitTime = EditorGUILayout.FloatField("Jump Down Exit Time", jumpDownExitTime);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Body", EditorStyles.boldLabel);
        playerRoot = (Transform)EditorGUILayout.ObjectField("Player Root", playerRoot, typeof(Transform), true);
        body = (GameObject)EditorGUILayout.ObjectField("Body (scene object or model)", body, typeof(GameObject), true);
        backOffset = EditorGUILayout.FloatField("Back Offset (local Z)", backOffset);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Build Controller", GUILayout.Height(26))) BuildController();
        if (GUILayout.Button("Setup Body", GUILayout.Height(26))) SetupBody();
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Do Both", GUILayout.Height(30)))
        {
            if (BuildController()) SetupBody();
        }
        if (GUILayout.Button("Create Pistol Override", GUILayout.Height(24))) BuildPistolOverride();

        EditorGUILayout.HelpBox("Build Controller rebuilds Base Layer and UpperBody; other layers are kept. " +
                                "Generated hold clips and the mask are kept once made (delete to regenerate). " +
                                "Setup Body re-places the body at Back Offset and is undoable.", MessageType.None);
    }

    // ===== Controller =====

    private bool BuildController()
    {
        string folder = clipFolder != null ? AssetDatabase.GetAssetPath(clipFolder) : null;
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError("[TPBodySetup] Assign the clip folder.");
            return false;
        }

        var clips = LoadClips(folder);
        var missing = new List<string>();

        if (controller == null)
        {
            string path = Path.GetDirectoryName(folder).Replace('\\', '/') + "/TP_Body.controller";
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        EnsureParam("MoveX", AnimatorControllerParameterType.Float);
        EnsureParam("MoveZ", AnimatorControllerParameterType.Float);
        EnsureParam("Crouch", AnimatorControllerParameterType.Float);
        EnsureParam("VerticalSpeed", AnimatorControllerParameterType.Float);
        EnsureParam("Grounded", AnimatorControllerParameterType.Bool);
        EnsureParam("AimBlend", AnimatorControllerParameterType.Float);

        var sm = controller.layers[0].stateMachine;
        ClearStateMachine(sm);
        RemoveOldTrees();
        BuildUpperLayer(clips, missing);

        // Locomotion: 1D on Crouch over two 2D trees
        var root = NewTree("Locomotion", BlendTreeType.Simple1D, "Crouch", null);
        var stand = NewTree("Stand", BlendTreeType.FreeformDirectional2D, "MoveX", "MoveZ");
        var crouch = NewTree("Crouch", BlendTreeType.FreeformDirectional2D, "MoveX", "MoveZ");
        Fill(stand, Stand, clips, missing);
        Fill(crouch, Crouch, clips, missing);
        root.useAutomaticThresholds = false;
        root.AddChild(stand, 0f);
        root.AddChild(crouch, 1f);

        var loco = sm.AddState("Locomotion", new Vector3(300f, 0f, 0f));
        loco.motion = root;
        sm.defaultState = loco;

        var up = ClipState(sm, "Jump Up", "jump up", new Vector3(600f, -80f, 0f), clips, missing);
        var loop = ClipState(sm, "Jump Loop", "jump loop", new Vector3(600f, 40f, 0f), clips, missing);
        var down = ClipState(sm, "Jump Down", "jump down", new Vector3(300f, 160f, 0f), clips, missing);

        var t = Link(loco, up, -1f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        t.AddCondition(AnimatorConditionMode.Greater, 0f, "VerticalSpeed");

        t = Link(loco, loop, -1f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        t.AddCondition(AnimatorConditionMode.Less, 0f, "VerticalSpeed");

        Link(up, loop, jumpUpExitTime);

        t = Link(up, down, -1f);
        t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        t = Link(loop, down, -1f);
        t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        t = Link(down, loco, jumpDownExitTime);
        t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        // Jump again straight off a landing
        t = Link(down, up, -1f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        t.AddCondition(AnimatorConditionMode.Greater, 0f, "VerticalSpeed");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        if (missing.Count > 0)
            Debug.LogWarning($"[TPBodySetup] Missing clips (skipped): {string.Join(", ", missing)}");
        Debug.Log($"[TPBodySetup] Built Base Layer and {UpperLayer} on '{AssetDatabase.GetAssetPath(controller)}'.");
        return true;
    }

    // Upper body: masked layer holding the weapon pose, hip to aim on AimBlend
    private void BuildUpperLayer(Dictionary<string, AnimationClip> clips, List<string> missing)
    {
        string dir = GeneratedDir();
        var hip = HoldClip(dir, HoldHip, HoldHipSource, clips, missing);
        var aim = HoldClip(dir, HoldAim, HoldAimSource, clips, missing);
        var mask = UpperMask(dir);

        var layers = controller.layers;
        int i = System.Array.FindIndex(layers, l => l.name == UpperLayer);
        if (i < 0)
        {
            var newSm = new AnimatorStateMachine { name = UpperLayer, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(newSm, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = UpperLayer,
                stateMachine = newSm,
                defaultWeight = 1f,
                avatarMask = mask
            });
            layers = controller.layers;
            i = layers.Length - 1;
        }
        else
        {
            layers[i].avatarMask = mask;
            layers[i].defaultWeight = 1f;
            controller.layers = layers;
        }

        var sm = controller.layers[i].stateMachine;
        ClearStateMachine(sm);

        var tree = NewTree("Hold", BlendTreeType.Simple1D, "AimBlend", null);
        tree.useAutomaticThresholds = false;
        if (hip != null) tree.AddChild(hip, 0f);
        if (aim != null) tree.AddChild(aim, 1f);

        var hold = sm.AddState("Hold", new Vector3(300f, 0f, 0f));
        hold.motion = tree;
        sm.defaultState = hold;
    }

    // Copies a source clip to its own asset so overrides can swap it alone
    private static AnimationClip HoldClip(string dir, string name, string source,
                                          Dictionary<string, AnimationClip> clips, List<string> missing)
    {
        string path = $"{dir}/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;

        if (!clips.TryGetValue(source, out var src))
        {
            missing.Add(source);
            return null;
        }

        var copy = Instantiate(src);
        copy.name = name;
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    // Spine, head and arms; no root or legs
    private static AvatarMask UpperMask(string dir)
    {
        string path = $"{dir}/TP_UpperBody.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask != null) return mask;

        mask = new AvatarMask();
        for (int p = 0; p < (int)AvatarMaskBodyPart.LastBodyPart; p++)
        {
            var part = (AvatarMaskBodyPart)p;
            bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                      || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                      || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                      || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        AssetDatabase.CreateAsset(mask, path);
        return mask;
    }

    // Pistol family: both hold poses become the pistol idle
    private void BuildPistolOverride()
    {
        if (controller == null)
        {
            Debug.LogError("[TPBodySetup] Build or assign the controller first.");
            return;
        }

        string folder = pistolFolder != null ? AssetDatabase.GetAssetPath(pistolFolder) : null;
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError("[TPBodySetup] Assign the pistol clip folder.");
            return;
        }
        if (!LoadClips(folder).TryGetValue(PistolHoldSource, out var pistol))
        {
            Debug.LogError($"[TPBodySetup] No '{PistolHoldSource}' clip in '{folder}'.");
            return;
        }

        string dir = GeneratedDir();
        var hip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{dir}/{HoldHip}.anim");
        var aim = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{dir}/{HoldAim}.anim");
        if (hip == null || aim == null)
        {
            Debug.LogError("[TPBodySetup] Hold clips missing; run Build Controller first.");
            return;
        }

        string path = $"{dir}/AOC_TP_Pistol.overrideController";
        var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
        if (aoc == null)
        {
            aoc = new AnimatorOverrideController(controller);
            AssetDatabase.CreateAsset(aoc, path);
        }
        aoc.runtimeAnimatorController = controller;

        var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(aoc.overridesCount);
        aoc.GetOverrides(list);
        for (int i = 0; i < list.Count; i++)
            if (list[i].Key == hip || list[i].Key == aim)
                list[i] = new KeyValuePair<AnimationClip, AnimationClip>(list[i].Key, pistol);
        aoc.ApplyOverrides(list);

        EditorUtility.SetDirty(aoc);
        AssetDatabase.SaveAssets();
        Debug.Log($"[TPBodySetup] Pistol override at '{path}'. Assign it as Third Person Controller on pistol viewmodels.");
    }

    private string GeneratedDir()
    {
        string parent = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller)).Replace('\\', '/');
        string dir = $"{parent}/{GeneratedFolder}";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(parent, GeneratedFolder);
        return dir;
    }

    private static Dictionary<string, AnimationClip> LoadClips(string folder)
    {
        var map = new Dictionary<string, AnimationClip>();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null) map[Path.GetFileNameWithoutExtension(p).ToLowerInvariant()] = clip;
        }
        return map;
    }

    private void EnsureParam(string name, AnimatorControllerParameterType type)
    {
        var ps = controller.parameters;
        int i = System.Array.FindIndex(ps, p => p.name == name);
        if (i >= 0 && ps[i].type == type) return;
        if (i >= 0) controller.RemoveParameter(i);
        controller.AddParameter(name, type);
    }

    private static void ClearStateMachine(AnimatorStateMachine sm)
    {
        foreach (var t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);
        foreach (var t in sm.entryTransitions.ToArray()) sm.RemoveEntryTransition(t);
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
        foreach (var s in sm.stateMachines.ToArray()) sm.RemoveStateMachine(s.stateMachine);
    }

    // Only trees this tool made, so other layers' trees survive
    private void RemoveOldTrees()
    {
        string path = AssetDatabase.GetAssetPath(controller);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o is BlendTree bt && bt.name.StartsWith(TreePrefix))
            {
                AssetDatabase.RemoveObjectFromAsset(bt);
                DestroyImmediate(bt, true);
            }
        }
    }

    private BlendTree NewTree(string name, BlendTreeType type, string paramX, string paramY)
    {
        var tree = new BlendTree
        {
            name = TreePrefix + name,
            blendType = type,
            blendParameter = paramX,
            hideFlags = HideFlags.HideInHierarchy
        };
        if (paramY != null) tree.blendParameterY = paramY;
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    private static void Fill(BlendTree tree, (string clip, float x, float y)[] rows,
                             Dictionary<string, AnimationClip> clips, List<string> missing)
    {
        foreach (var r in rows)
        {
            if (clips.TryGetValue(r.clip, out var clip)) tree.AddChild(clip, new Vector2(r.x, r.y));
            else missing.Add(r.clip);
        }
    }

    private static AnimatorState ClipState(AnimatorStateMachine sm, string state, string clip, Vector3 pos,
                                           Dictionary<string, AnimationClip> clips, List<string> missing)
    {
        var s = sm.AddState(state, pos);
        if (clips.TryGetValue(clip, out var c)) s.motion = c;
        else missing.Add(clip);
        return s;
    }

    // exitTime < 0 means no exit time
    private AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float exitTime)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = exitTime >= 0f;
        if (t.hasExitTime) t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = transitionTime;
        return t;
    }

    // ===== Body =====

    private void SetupBody()
    {
        if (playerRoot == null || body == null)
        {
            Debug.LogError("[TPBodySetup] Assign Player Root and Body.");
            return;
        }
        if (controller == null)
        {
            Debug.LogError("[TPBodySetup] Build or assign the controller first.");
            return;
        }

        const string undo = "TP Body Setup";
        GameObject go = body;
        if (EditorUtility.IsPersistent(body))
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(body);
            Undo.RegisterCreatedObjectUndo(go, undo);
        }

        // Parent and place: feet at the controller bottom, pushed back
        Undo.SetTransformParent(go.transform, playerRoot, undo);
        var cc = playerRoot.GetComponent<CharacterController>();
        float feet = cc != null ? cc.center.y - cc.height * 0.5f : 0f;
        Undo.RecordObject(go.transform, undo);
        go.transform.localPosition = new Vector3(0f, feet, backOffset);
        go.transform.localRotation = Quaternion.identity;

        // No colliders, Default layer
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(col);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Undo.DestroyObjectImmediate(rb);
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(t.gameObject, undo);
            t.gameObject.layer = 0;
        }

        // Animator
        var anim = GetOrAdd<Animator>(go);
        Undo.RecordObject(anim, undo);
        anim.runtimeAnimatorController = controller;
        if (anim.avatar == null) anim.avatar = FindAvatar(go);
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (anim.avatar == null) Debug.LogWarning("[TPBodySetup] No avatar found; assign it on the Animator.");

        GetOrAdd<PlayerPresentation>(playerRoot.gameObject);
        GetOrAdd<ThirdPersonBodyAnimator>(go);
        var vis = GetOrAdd<BodyVisibility>(go);
        GetOrAdd<ThirdPersonWeapon>(go);

        // Legs mesh by name, everything else is full body
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var legs = smrs.Where(r => r.name.ToLowerInvariant().Contains("legs")).ToArray();
        var full = smrs.Except(legs).ToArray();
        foreach (var r in legs)
        {
            Undo.RecordObject(r, undo);
            r.updateWhenOffscreen = true;
        }

        var so = new SerializedObject(vis);
        SetArray(so.FindProperty("fullBody"), full);
        SetArray(so.FindProperty("legs"), legs);
        so.ApplyModifiedProperties();

        if (legs.Length == 0) Debug.LogWarning("[TPBodySetup] No mesh named 'Legs' found under the body.");
        Selection.activeGameObject = go;
        Debug.Log($"[TPBodySetup] Body '{go.name}' set up under '{playerRoot.name}' " +
                  $"({full.Length} full body, {legs.Length} legs renderer(s)).");
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    // Avatar from the model the body came from
    private static Avatar FindAvatar(GameObject go)
    {
        var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
        string path = source != null ? AssetDatabase.GetAssetPath(source) : null;
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
    }

    private static void SetArray(SerializedProperty prop, Object[] items)
    {
        if (prop == null) return;
        prop.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }
}
