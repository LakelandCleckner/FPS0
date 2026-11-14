using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Combat.Enemies;
using Combat.Stats;
using Combat.Weapons;

// Hit reaction setup in one window: clip avatars, stagger stat, profile, HitReact layer, pistol override, enemy
public class EnemyHitReactionSetup : EditorWindow
{
    private const string LayerName = "HitReact";
    private const string ClipsRoot = "Assets/Animations/Enemies/Mixamo/Imports/Hit Reactions";
    private const string BodyControllerPath = "Assets/Animations/Player/ThirdPerson Weapon Packs/TP_Body.controller";
    private const string ZombieControllerPath = "Assets/Animations/Enemies/Mixamo/Imports/Alien_Zombie/Ch44_nonPBR@Zombie Idle.controller";
    private const string PistolOverridePath = "Assets/Animations/Player/ThirdPerson Weapon Packs/TP_Generated/AOC_TP_Pistol.overrideController";
    private const string StatPath = "Assets/GameData/Stats/Definitions/Weapon/Weapon Stagger Power.asset";
    private const string ProfilePath = "Assets/GameData/Enemies/HitReactions/HitReaction_Default.asset";

    private static readonly string[] RoleLabels = { "Stand", "Walk", "Run", "Crouch", "Heavy" };

    [SerializeField] private HitReactionProfileSO profile;
    [SerializeField] private AnimatorController controller;
    [SerializeField] private DefaultAsset clipFolder;
    [SerializeField] private AnimationClip[] clips = new AnimationClip[5];
    [SerializeField] private AnimatorOverrideController pistolOverride;
    [SerializeField] private DefaultAsset pistolFolder;
    [SerializeField] private AnimationClip pistolStand, pistolHeavy;
    [SerializeField] private GameObject enemy;
    [SerializeField] private EnemyArchetypeSO archetype;
    private readonly List<string> report = new List<string>();
    private Vector2 scroll;

    [MenuItem("Tools/Enemy Hit Reaction Setup")]
    private static void Open() => GetWindow<EnemyHitReactionSetup>("Enemy Hit Reactions");

    private void OnEnable()
    {
        if (profile == null) profile = AssetDatabase.LoadAssetAtPath<HitReactionProfileSO>(ProfilePath);
        if (controller == null) controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BodyControllerPath);
        if (clipFolder == null) clipFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ClipsRoot + "/Rifle");
        if (pistolOverride == null) pistolOverride = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(PistolOverridePath);
        if (pistolFolder == null) pistolFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ClipsRoot + "/Pistol");
        if (clips == null || clips.Length != RoleLabels.Length) clips = new AnimationClip[RoleLabels.Length];
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        Section("1. Clip avatars");
        EditorGUILayout.LabelField("Hit clips must be Humanoid with a T-pose rest pose, or hands and arms roll on the body.", EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("Open Fix Clip Avatars with the Hit Reactions folder"))
            ClipAvatarFixer.Open(AssetDatabase.LoadAssetAtPath<DefaultAsset>(ClipsRoot));

        Section("2. Stagger stat");
        EditorGUILayout.LabelField("Creates stagger_power (1 = normal), sets it on every WeaponStatKeys asset and registers it with the open scene's StatBootstrap.", EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("Create / Update Stagger Stat")) Run(CreateStat);

        Section("3. Profile");
        profile = (HitReactionProfileSO)EditorGUILayout.ObjectField("Profile", profile, typeof(HitReactionProfileSO), false);
        if (profile == null && GUILayout.Button("Create Default Profile")) Run(CreateProfile);

        Section("4. HitReact layer");
        controller = (AnimatorController)EditorGUILayout.ObjectField("Controller", controller, typeof(AnimatorController), false);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("TP Body (shooters)")) { controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BodyControllerPath); clipFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ClipsRoot + "/Rifle"); FillFromFolder(); }
        if (GUILayout.Button("Zombie")) { controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ZombieControllerPath); clipFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ClipsRoot + "/Zombie"); FillFromFolder(); }
        EditorGUILayout.EndHorizontal();
        if (controller != null && AssetDatabase.GetAssetPath(controller) == BodyControllerPath)
            EditorGUILayout.HelpBox("TP_Body is also the player's controller. The layer stays at weight 0 there.", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        clipFolder = (DefaultAsset)EditorGUILayout.ObjectField("Clip Folder", clipFolder, typeof(DefaultAsset), false);
        if (GUILayout.Button("Fill", GUILayout.Width(50))) FillFromFolder();
        EditorGUILayout.EndHorizontal();
        for (int i = 0; i < RoleLabels.Length; i++)
        {
            clips[i] = (AnimationClip)EditorGUILayout.ObjectField(RoleLabels[i], clips[i], typeof(AnimationClip), false);
            ClipWarning(clips[i]);
        }
        if (clips[0] == null) EditorGUILayout.HelpBox("Stand is required: every missing clip falls back to it.", MessageType.Warning);
        GUI.enabled = controller != null && clips[0] != null;
        if (GUILayout.Button("Build / Rebuild HitReact Layer", GUILayout.Height(24))) Run(BuildLayer);
        GUI.enabled = true;

        Section("5. Pistol override");
        pistolOverride = (AnimatorOverrideController)EditorGUILayout.ObjectField("Override", pistolOverride, typeof(AnimatorOverrideController), false);
        EditorGUILayout.BeginHorizontal();
        pistolFolder = (DefaultAsset)EditorGUILayout.ObjectField("Clip Folder", pistolFolder, typeof(DefaultAsset), false);
        if (GUILayout.Button("Fill", GUILayout.Width(50))) FillPistol();
        EditorGUILayout.EndHorizontal();
        pistolStand = (AnimationClip)EditorGUILayout.ObjectField(new GUIContent("Stand", "Also used for walk, run and crouch."), pistolStand, typeof(AnimationClip), false);
        ClipWarning(pistolStand);
        pistolHeavy = (AnimationClip)EditorGUILayout.ObjectField("Heavy", pistolHeavy, typeof(AnimationClip), false);
        ClipWarning(pistolHeavy);
        GUI.enabled = pistolOverride != null && pistolStand != null;
        if (GUILayout.Button("Apply Pistol Hit Clips")) Run(ApplyPistol);
        GUI.enabled = true;

        Section("6. Enemy");
        enemy = (GameObject)EditorGUILayout.ObjectField("Enemy (prefab or scene)", enemy, typeof(GameObject), true);
        archetype = (EnemyArchetypeSO)EditorGUILayout.ObjectField(new GUIContent("Archetype (optional)", "Gets the profile if it has none."), archetype, typeof(EnemyArchetypeSO), false);
        GUI.enabled = enemy != null;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Validate")) Run(Validate);
        if (GUILayout.Button("Add Hit Reaction")) Run(AddHitReaction);
        EditorGUILayout.EndHorizontal();
        GUI.enabled = true;

        if (report.Count > 0)
        {
            Section("Report");
            foreach (var line in report)
            {
                var type = line.StartsWith("!") ? MessageType.Error : line.StartsWith("?") ? MessageType.Warning : MessageType.None;
                EditorGUILayout.HelpBox(line.TrimStart('!', '?', ' '), type);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private static void Section(string title)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    private void Run(System.Action step)
    {
        report.Clear();
        step();
        Repaint();
    }

    private static void ClipWarning(AnimationClip clip)
    {
        if (clip == null) return;
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as ModelImporter;
        if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            EditorGUILayout.HelpBox("Not Humanoid: run Fix Clip Avatars on it (step 1).", MessageType.Warning);
        else if (clip.isLooping)
            EditorGUILayout.HelpBox("Loops: hit clips should be one-shots (Fix Clip Avatars turns looping off).", MessageType.Warning);
    }

    // ---- 2. stat ----

    private void CreateStat()
    {
        var def = AssetDatabase.LoadAssetAtPath<StatDefinitionSO>(StatPath);
        if (def == null)
        {
            EnsureFolder(Path.GetDirectoryName(StatPath).Replace('\\', '/'));
            def = CreateInstance<StatDefinitionSO>();
            def.id = "stagger_power";
            def.displayName = "Stagger Power";
            def.resolutionMode = StatResolutionMode.ScaledBase;
            def.defaultValue = 1f;
            def.useMin = true;
            def.minValue = 0f;
            def.displayDecimals = 2;
            AssetDatabase.CreateAsset(def, StatPath);
            report.Add("Created " + StatPath);
        }

        foreach (var guid in AssetDatabase.FindAssets("t:WeaponStatKeys"))
        {
            var keys = AssetDatabase.LoadAssetAtPath<WeaponStatKeys>(AssetDatabase.GUIDToAssetPath(guid));
            if (keys == null || keys.staggerPower == def) continue;
            keys.staggerPower = def;
            EditorUtility.SetDirty(keys);
            report.Add("Set staggerPower on " + AssetDatabase.GetAssetPath(keys));
        }
        AssetDatabase.SaveAssets();

        var boots = Object.FindObjectsByType<StatBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (boots.Length == 0) { report.Add("? No StatBootstrap in the open scene: add stagger_power to it by hand."); return; }
        foreach (var boot in boots)
        {
            var so = new SerializedObject(boot);
            var list = so.FindProperty("statDefinitions");
            bool has = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == def) { has = true; break; }
            if (has) { report.Add($"StatBootstrap '{boot.name}': already registered."); continue; }
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = def;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(boot.gameObject.scene);
            report.Add($"StatBootstrap '{boot.name}': registered (save the scene).");
        }
    }

    // ---- 3. profile ----

    private void CreateProfile()
    {
        profile = AssetDatabase.LoadAssetAtPath<HitReactionProfileSO>(ProfilePath);
        if (profile != null) { report.Add("Kept existing " + ProfilePath); return; }
        EnsureFolder(Path.GetDirectoryName(ProfilePath).Replace('\\', '/'));
        profile = CreateInstance<HitReactionProfileSO>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        AssetDatabase.SaveAssets();
        report.Add("Created " + ProfilePath);
    }

    // ---- 4. layer ----

    // By name: Large = heavy, then running / walking / crouching / standing
    private void FillFromFolder()
    {
        clips = new AnimationClip[RoleLabels.Length];
        foreach (var (name, clip) in FolderClips(clipFolder))
        {
            if (name.Contains("large")) clips[4] = clip;
            else if (name.Contains("running")) clips[2] = clip;
            else if (name.Contains("walking")) clips[1] = clip;
            else if (name.Contains("crouching")) clips[3] = clip;
            else if (name.Contains("standing")) clips[0] = clip;
        }
    }

    private void FillPistol()
    {
        pistolStand = pistolHeavy = null;
        foreach (var (name, clip) in FolderClips(pistolFolder))
        {
            if (name.Contains("large")) pistolHeavy = clip;
            else pistolStand = clip;
        }
    }

    private static IEnumerable<(string, AnimationClip)> FolderClips(DefaultAsset folder)
    {
        string path = folder != null ? AssetDatabase.GetAssetPath(folder) : null;
        if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) yield break;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { path }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var clip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null) yield return (Path.GetFileNameWithoutExtension(p).ToLowerInvariant(), clip);
        }
    }

    // Top layer, full body, override, weight 0 (the component drives it); an empty state plus one per clip
    private void BuildLayer()
    {
        Undo.RegisterCompleteObjectUndo(controller, "HitReact Layer");
        EnsureParam(controller, EnemyHitReaction.SpeedParam, 1f);

        var layers = controller.layers;
        int i = System.Array.FindIndex(layers, l => l.name == LayerName);
        if (i < 0)
        {
            var sm = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            controller.AddLayer(new AnimatorControllerLayer { name = LayerName, stateMachine = sm });
            layers = controller.layers;
            i = layers.Length - 1;
            report.Add("Added the HitReact layer");
        }

        // Last, so it overrides every other layer
        var layer = layers[i];
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.avatarMask = null;
        layer.iKPass = false;
        var list = layers.ToList();
        list.RemoveAt(i);
        list.Add(layer);
        controller.layers = list.ToArray();

        var machine = layer.stateMachine;
        foreach (var t in machine.anyStateTransitions.ToArray()) machine.RemoveAnyStateTransition(t);
        foreach (var s in machine.states.ToArray()) machine.RemoveState(s.state);

        var none = machine.AddState("None", new Vector3(300f, -60f, 0f));
        none.writeDefaultValues = false;
        machine.defaultState = none;
        for (int r = 0; r < RoleLabels.Length; r++)
        {
            if (clips[r] == null) continue;
            var st = machine.AddState(EnemyHitReaction.StateNames[r], new Vector3(300f, r * 60f, 0f));
            st.motion = clips[r];
            st.speedParameterActive = true;
            st.speedParameter = EnemyHitReaction.SpeedParam;
            st.writeDefaultValues = true;
            st.iKOnFeet = true;
            report.Add($"{EnemyHitReaction.StateNames[r]} <- {clips[r].name}");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        report.Insert(0, $"Built the HitReact layer on '{controller.name}'.");
    }

    private static void EnsureParam(AnimatorController ctrl, string name, float def)
    {
        var ps = ctrl.parameters;
        int idx = System.Array.FindIndex(ps, p => p.name == name);
        if (idx >= 0 && ps[idx].type == AnimatorControllerParameterType.Float) return;
        if (idx >= 0) ctrl.RemoveParameter(idx);
        ctrl.AddParameter(new AnimatorControllerParameter { name = name, type = AnimatorControllerParameterType.Float, defaultFloat = def });
    }

    // ---- 5. pistol ----

    private void ApplyPistol()
    {
        var baseCtrl = pistolOverride.runtimeAnimatorController as AnimatorController;
        if (baseCtrl == null) { report.Add("! The override has no base controller."); return; }
        var layer = baseCtrl.layers.FirstOrDefault(l => l.name == LayerName);
        if (layer == null) { report.Add($"! '{baseCtrl.name}' has no HitReact layer: build it first (step 4)."); return; }

        var byState = layer.stateMachine.states.ToDictionary(s => s.state.name, s => s.state.motion as AnimationClip);
        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(pistolOverride.overridesCount);
        pistolOverride.GetOverrides(pairs);
        int set = 0;
        for (int i = 0; i < pairs.Count; i++)
        {
            for (int r = 0; r < RoleLabels.Length; r++)
            {
                if (!byState.TryGetValue(EnemyHitReaction.StateNames[r], out var original) || original != pairs[i].Key) continue;
                var clip = r == 4 && pistolHeavy != null ? pistolHeavy : pistolStand;
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clip);
                set++;
                break;
            }
        }
        pistolOverride.ApplyOverrides(pairs);
        EditorUtility.SetDirty(pistolOverride);
        AssetDatabase.SaveAssets();
        report.Add($"Set {set} hit clip(s) on '{pistolOverride.name}'.");
    }

    // ---- 6. enemy ----

    private void Validate()
    {
        var issues = new List<string>();
        var go = enemy;
        if (go.GetComponent<CombatantHealth>() == null) issues.Add("No CombatantHealth on this object: pick the enemy root");
        var hr = go.GetComponent<EnemyHitReaction>();
        if (hr == null) issues.Add("No EnemyHitReaction (Add Hit Reaction)");
        else
        {
            var def = go.GetComponent<EnemyDefinitionApplier>();
            var arch = def != null ? def.Archetype : null;
            var fallback = new SerializedObject(hr).FindProperty("defaultProfile").objectReferenceValue;
            if (fallback == null && (arch == null || arch.hitReactionProfile == null)) issues.Add("No profile: set one on the archetype or the component's default");
        }

        var anim = go.GetComponentInChildren<Animator>();
        if (anim == null) issues.Add("No Animator in children");
        else
        {
            if (anim.avatar == null || !anim.avatar.isHuman) issues.Add("? Animator avatar is not Humanoid: the kick finds bones by Mixamo names");
            var ctrl = anim.runtimeAnimatorController is AnimatorOverrideController aoc
                ? aoc.runtimeAnimatorController as AnimatorController
                : anim.runtimeAnimatorController as AnimatorController;
            if (ctrl == null) issues.Add("No animator controller");
            else
            {
                var layer = ctrl.layers.FirstOrDefault(l => l.name == LayerName);
                if (layer == null) issues.Add($"'{ctrl.name}' has no HitReact layer: only the kick will play (step 4)");
                else
                {
                    if (ctrl.layers[ctrl.layers.Length - 1].name != LayerName) issues.Add("? HitReact isn't the last layer: rebuild it (step 4)");
                    if (layer.stateMachine.states.All(s => s.state.name != EnemyHitReaction.StateNames[0])) issues.Add("HitReact layer has no Hit_Stand state");
                    foreach (var s in layer.stateMachine.states)
                    {
                        var imp = s.state.motion != null ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(s.state.motion)) as ModelImporter : null;
                        if (imp != null && imp.animationType != ModelImporterAnimationType.Human) issues.Add($"? {s.state.name}: clip is not Humanoid (step 1)");
                    }
                }
            }
        }

        if (issues.Count == 0) report.Add("No problems found.");
        else foreach (var i in issues) report.Add(i.StartsWith("?") ? i : "! " + i);
    }

    private void AddHitReaction()
    {
        string prefabPath = PrefabUtility.IsPartOfPrefabAsset(enemy) ? AssetDatabase.GetAssetPath(enemy) : null;
        var root = prefabPath != null ? PrefabUtility.LoadPrefabContents(prefabPath) : enemy;
        try
        {
            if (root.GetComponent<CombatantHealth>() == null) { report.Add("! No CombatantHealth on this object: pick the enemy root."); return; }
            var hr = root.GetComponent<EnemyHitReaction>();
            if (hr == null) { hr = Undo.AddComponent<EnemyHitReaction>(root); report.Add("Added EnemyHitReaction"); }
            var so = new SerializedObject(hr);
            var prop = so.FindProperty("defaultProfile");
            if (prop.objectReferenceValue == null && profile != null) { prop.objectReferenceValue = profile; report.Add("Default profile: " + profile.name); }
            so.ApplyModifiedProperties();

            if (prefabPath != null) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            else EditorUtility.SetDirty(root);
        }
        finally
        {
            if (prefabPath != null) PrefabUtility.UnloadPrefabContents(root);
        }

        if (archetype != null && archetype.hitReactionProfile == null && profile != null)
        {
            Undo.RecordObject(archetype, "Hit Reaction Profile");
            archetype.hitReactionProfile = profile;
            EditorUtility.SetDirty(archetype);
            AssetDatabase.SaveAssets();
            report.Add($"Archetype '{archetype.name}': profile set");
        }
        Validate();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
