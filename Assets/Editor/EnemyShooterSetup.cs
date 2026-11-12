using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Combat.AI;
using Combat.Delivery;
using Combat.Enemies;
using Combat.Stats;
using Combat.Weapons;

// Shooter setup in one window: ranged stats, Humanoid clips, controller, content and enemy components, each step validated.
public class EnemyShooterSetup : EditorWindow
{
    private const string Root = "Assets/Animations/Enemies/Shooter";
    private const string ClipFolder = Root + "/Clips";
    private const string ControllerPath = Root + "/Shooter.controller";
    private const string MaskPath = Root + "/ShooterUpperBody.mask";
    private const string StatFolder = "Assets/GameData/Stats/Definitions/Enemy AI/Ranged";
    private const string KeysPath = "Assets/GameData/Stats/Keys/EnemyRangedStatKeys.asset";
    private const string ContentFolder = "Assets/GameData/Enemies/Ranged";
    private const string BehaviourPath = "Assets/GameData/Enemies/Behaviours/Behaviour_Rifleman.asset";
    private const string SourceProjectile = "Assets/GameData/Weapons/_Shared/Projectiles/Projectile_Standard.asset";

    // Clip role, source file under the pack folder, loops
    private struct ClipSpec
    {
        public string Role;
        public string Source;
        public bool Loop;
        public ClipSpec(string role, string source, bool loop) { Role = role; Source = source; Loop = loop; }
    }

    private static readonly string[] Dirs =
        { "forward", "forward left", "forward right", "left", "right", "backward", "backward left", "backward right" };

    private static readonly Vector2[] DirVectors =
    {
        new Vector2(0f, 1f), new Vector2(-0.7071f, 0.7071f), new Vector2(0.7071f, 0.7071f), new Vector2(-1f, 0f),
        new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-0.7071f, -0.7071f), new Vector2(0.7071f, -0.7071f)
    };

    private static readonly (string id, string field, string display, bool lowerIsBetter, bool unit)[] StatSpecs =
    {
        ("ranged_damage", "damage", "Ranged Damage", false, false),
        ("ranged_fire_rate", "fireRate", "Ranged Fire Rate", false, false),
        ("ranged_burst_size", "burstSize", "Ranged Burst Size", false, false),
        ("ranged_burst_separation", "burstSeparation", "Ranged Burst Separation", true, false),
        ("ranged_aim_error", "aimError", "Ranged Aim Error", true, false),
        ("ranged_aim_error_min", "aimErrorMin", "Ranged Aim Error Min", true, false),
        ("ranged_aim_settle_time", "aimSettleTime", "Ranged Aim Settle Time", true, false),
        ("ranged_burst_spread", "burstSpread", "Ranged Burst Spread", true, false),
        ("ranged_target_delay", "targetDelay", "Ranged Target Delay", true, false),
        ("ranged_tracking", "tracking", "Ranged Tracking", false, true),
        ("ranged_leading", "leading", "Ranged Leading", false, true),
        ("ranged_projectile_speed", "projectileSpeed", "Ranged Projectile Speed", false, false),
        ("ranged_max_range", "maxRange", "Ranged Max Range", false, false),
        ("ranged_magazine", "magazine", "Ranged Magazine", false, false),
        ("ranged_reload_time", "reloadTime", "Ranged Reload Time", true, false),
        ("ranged_windup", "windup", "Ranged Windup", true, false),
    };

    private string packFolder = "Assets/Animations/Player/ThirdPerson Weapon Packs";
    private float walkSpeed = 1.5f;
    private float runSpeed = 3.5f;
    private float crouchSpeed = 1.1f;
    private float raiseBlend = 0.25f;
    private float fireHold = 0.25f;
    private GameObject enemy;
    private WeaponSO weapon;
    private bool assignController = true;
    private readonly List<string> report = new List<string>();
    private Vector2 scroll;

    [MenuItem("Tools/Enemy Shooter Setup")]
    private static void Open() => GetWindow<EnemyShooterSetup>("Enemy Shooter Setup");

    private static List<ClipSpec> Specs()
    {
        var list = new List<ClipSpec>
        {
            new ClipSpec("Idle", "Rifle Locomotion/idle.fbx", true),
            new ClipSpec("IdleAim", "Rifle Locomotion/idle aiming.fbx", true),
            new ClipSpec("CrouchIdle", "Rifle Locomotion/idle crouching.fbx", true),
            new ClipSpec("CrouchAim", "Rifle Locomotion/idle crouching aiming.fbx", true),
            new ClipSpec("Fire", "RifleAdditions/firing rifle.fbx", false),
            new ClipSpec("Reload", "RifleAdditions/reloading.fbx", false),
        };
        foreach (var d in Dirs)
        {
            list.Add(new ClipSpec("Walk " + d, $"Rifle Locomotion/walk {d}.fbx", true));
            list.Add(new ClipSpec("Run " + d, $"Rifle Locomotion/run {d}.fbx", true));
            list.Add(new ClipSpec("CrouchWalk " + d, $"Rifle Locomotion/walk crouching {d}.fbx", true));
        }
        return list;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        Section("1. Ranged stats");
        EditorGUILayout.LabelField("Creates the ranged_ stat definitions, the EnemyRangedStatKeys asset, and registers them with the open scene's StatBootstrap.", EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("Create / Update Ranged Stats")) Run(CreateStats);

        Section("2. Humanoid clip copies");
        packFolder = EditorGUILayout.TextField("Rifle Pack Folder", packFolder);
        var missing = Specs().Where(s => !File.Exists(Path.Combine(packFolder, s.Source))).Select(s => s.Source).ToList();
        if (missing.Count > 0) EditorGUILayout.HelpBox("Missing in the pack folder:\n" + string.Join("\n", missing), MessageType.Error);
        GUI.enabled = missing.Count == 0;
        if (GUILayout.Button("Copy Clips as Humanoid")) Run(CopyClips);
        GUI.enabled = true;

        Section("3. Shooter controller");
        walkSpeed = EditorGUILayout.FloatField("Walk Speed (m/s)", walkSpeed);
        runSpeed = EditorGUILayout.FloatField("Run Speed (m/s)", runSpeed);
        crouchSpeed = EditorGUILayout.FloatField("Crouch Walk Speed (m/s)", crouchSpeed);
        raiseBlend = EditorGUILayout.FloatField(new GUIContent("Raise Blend (s)", "Lowered to aimed: the visible wind-up."), raiseBlend);
        fireHold = EditorGUILayout.FloatField(new GUIContent("Fire Hold (s)", "Fire pose held after the last shot."), fireHold);
        var clipsMissing = Specs().Where(s => LoadClip(s.Role) == null).Select(s => s.Role).ToList();
        if (clipsMissing.Count > 0) EditorGUILayout.HelpBox("Copy the clips first. Missing: " + string.Join(", ", clipsMissing), MessageType.Warning);
        GUI.enabled = clipsMissing.Count == 0;
        if (GUILayout.Button("Build / Rebuild Controller")) Run(BuildController);
        GUI.enabled = true;

        Section("4. Content");
        EditorGUILayout.LabelField("Behaviour_Rifleman profile, enemy bolt projectile and its delivery.", EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("Create Rifleman Content")) Run(CreateContent);

        Section("5. Enemy");
        enemy = (GameObject)EditorGUILayout.ObjectField("Enemy (prefab or scene)", enemy, typeof(GameObject), true);
        weapon = (WeaponSO)EditorGUILayout.ObjectField(new GUIContent("Weapon", "Held model: the viewmodel gun prefab, placed by its TP grip."), weapon, typeof(WeaponSO), false);
        assignController = EditorGUILayout.Toggle("Assign Shooter Controller", assignController);
        GUI.enabled = enemy != null;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Validate")) Run(Validate);
        if (GUILayout.Button("Add Shooter Components")) Run(AddComponents);
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

    // ---- 1. stats ----

    private void CreateStats()
    {
        EnsureFolder(StatFolder);
        var keys = AssetDatabase.LoadAssetAtPath<EnemyRangedStatKeys>(KeysPath);
        if (keys == null)
        {
            keys = CreateInstance<EnemyRangedStatKeys>();
            AssetDatabase.CreateAsset(keys, KeysPath);
            report.Add("Created " + KeysPath);
        }

        var so = new SerializedObject(keys);
        var defs = new List<StatDefinitionSO>();
        foreach (var s in StatSpecs)
        {
            string path = $"{StatFolder}/{s.id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<StatDefinitionSO>(path);
            if (def == null)
            {
                def = CreateInstance<StatDefinitionSO>();
                def.id = s.id;
                def.displayName = s.display;
                def.resolutionMode = StatResolutionMode.ScaledBase;
                def.useMin = true;
                def.minValue = 0f;
                def.useMax = s.unit;
                def.maxValue = 1f;
                def.lowerIsBetter = s.lowerIsBetter;
                def.displayDecimals = 2;
                AssetDatabase.CreateAsset(def, path);
                report.Add("Created " + path);
            }
            so.FindProperty(s.field).objectReferenceValue = def;
            defs.Add(def);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(keys);
        AssetDatabase.SaveAssets();

        var boots = Object.FindObjectsByType<StatBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (boots.Length == 0) { report.Add("? No StatBootstrap in the open scene: add the ranged stats to it by hand."); return; }
        foreach (var boot in boots)
        {
            var bso = new SerializedObject(boot);
            var list = bso.FindProperty("statDefinitions");
            int added = 0;
            foreach (var def in defs)
            {
                bool has = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == def) { has = true; break; }
                if (has) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = def;
                added++;
            }
            bso.ApplyModifiedProperties();
            if (added > 0) EditorSceneManager.MarkSceneDirty(boot.gameObject.scene);
            report.Add($"StatBootstrap '{boot.name}': {added} added (save the scene).");
        }
    }

    // ---- 2. clips ----

    private void CopyClips()
    {
        EnsureFolder(ClipFolder);
        foreach (var s in Specs())
        {
            string src = Path.Combine(packFolder, s.Source).Replace('\\', '/');
            string dst = $"{ClipFolder}/Shooter_{s.Role}.fbx";
            if (!File.Exists(dst) && !AssetDatabase.CopyAsset(src, dst)) { report.Add("! Copy failed: " + src); continue; }

            var importer = (ModelImporter)AssetImporter.GetAtPath(dst);
            // Each clip keeps its own Humanoid avatar; retargeting to any body goes through the Humanoid mapping
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.SaveAndReimport();

            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) { report.Add("! No clip in " + dst); continue; }
            foreach (var c in clips)
            {
                c.name = s.Role;
                c.lockRootRotation = true;          // bake rotation
                c.keepOriginalOrientation = false;
                c.lockRootHeightY = true;           // bake Y
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;       // XZ goes to root motion, which is off: no drift and snap on loop
                c.loopTime = s.Loop;
                c.loopPose = s.Loop;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            report.Add($"{s.Role} <- {s.Source}");
        }
    }

    private static AnimationClip LoadClip(string role)
    {
        string path = $"{ClipFolder}/Shooter_{role}.fbx";
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    // ---- 3. controller ----

    private void BuildController()
    {
        EnsureFolder(Root);
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // Rebuild in place so references to the asset stay valid
        while (ctrl.layers.Length > 0) ctrl.RemoveLayer(0);
        foreach (var p in ctrl.parameters) ctrl.RemoveParameter(p);

        ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter(new AnimatorControllerParameter { name = "ReloadSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        ctrl.AddParameter("Flinch", AnimatorControllerParameterType.Trigger);

        // Base: 2D strafe locomotion, standing and crouched; IK pass for look-at
        ctrl.AddLayer("Base Layer");
        var layers = ctrl.layers;
        layers[0].iKPass = true;
        ctrl.layers = layers;
        var sm = ctrl.layers[0].stateMachine;

        var stand = ctrl.CreateBlendTreeInController("Stand", out var standTree, 0);
        Strafe(standTree, LoadClip("IdleAim"), "Walk ", walkSpeed, "Run ", runSpeed);
        var crouch = ctrl.CreateBlendTreeInController("Crouch", out var crouchTree, 0);
        Strafe(crouchTree, LoadClip("CrouchAim"), "CrouchWalk ", crouchSpeed, null, 0f);
        sm.defaultState = stand;
        Transition(stand, crouch, 0.2f, "Crouch", true);
        Transition(crouch, stand, 0.2f, "Crouch", false);

        // Upper body: lowered / aimed (the raise is the wind-up), fire, reload
        var mask = BuildMask();
        ctrl.AddLayer("UpperBody");
        layers = ctrl.layers;
        layers[1].defaultWeight = 1f;
        layers[1].avatarMask = mask;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        ctrl.layers = layers;
        var upper = ctrl.layers[1].stateMachine;

        var lowered = upper.AddState("Lowered");
        lowered.motion = LoadClip("Idle");
        var aim = upper.AddState("Aim");
        aim.motion = LoadClip("IdleAim");
        var fireClip = LoadClip("Fire");
        var fire = upper.AddState("Fire");
        fire.motion = fireClip;
        var reload = upper.AddState("Reload");
        reload.motion = LoadClip("Reload");
        reload.speedParameterActive = true;
        reload.speedParameter = "ReloadSpeed";
        upper.defaultState = lowered;

        Transition(lowered, aim, raiseBlend, "Aiming", true);
        Transition(aim, lowered, 0.3f, "Aiming", false);

        var toFire = upper.AddAnyStateTransition(fire);
        toFire.AddCondition(AnimatorConditionMode.If, 0f, "Fire");
        toFire.hasExitTime = false;
        toFire.duration = 0.05f;
        toFire.canTransitionToSelf = true;

        var fromFire = fire.AddTransition(aim);
        fromFire.hasExitTime = true;
        fromFire.exitTime = fireClip != null && fireClip.length > 0f ? Mathf.Clamp01(fireHold / fireClip.length) : 0.25f;
        fromFire.duration = 0.1f;

        var toReload = upper.AddAnyStateTransition(reload);
        toReload.AddCondition(AnimatorConditionMode.If, 0f, "Reload");
        toReload.hasExitTime = false;
        toReload.duration = 0.1f;
        toReload.canTransitionToSelf = false;

        var reloadToAim = reload.AddTransition(aim);
        reloadToAim.hasExitTime = true;
        reloadToAim.exitTime = 0.95f;
        reloadToAim.duration = 0.1f;
        reloadToAim.AddCondition(AnimatorConditionMode.If, 0f, "Aiming");
        var reloadToLowered = reload.AddTransition(lowered);
        reloadToLowered.hasExitTime = true;
        reloadToLowered.exitTime = 0.95f;
        reloadToLowered.duration = 0.2f;
        reloadToLowered.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        report.Add("Built " + ControllerPath);
    }

    // Aim pose at the centre, walk ring, optional run ring
    private static void Strafe(BlendTree tree, AnimationClip center, string ringA, float speedA, string ringB, float speedB)
    {
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveZ";
        tree.AddChild(center, Vector2.zero);
        for (int i = 0; i < Dirs.Length; i++) tree.AddChild(LoadClip(ringA + Dirs[i]), DirVectors[i] * speedA);
        if (ringB == null) return;
        for (int i = 0; i < Dirs.Length; i++) tree.AddChild(LoadClip(ringB + Dirs[i]), DirVectors[i] * speedB);
    }

    private static void Transition(AnimatorState from, AnimatorState to, float duration, string param, bool on)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = duration;
        t.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
    }

    // Spine, head and arms; legs and root stay with the base layer
    private static AvatarMask BuildMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            bool on = part != AvatarMaskBodyPart.Root && part != AvatarMaskBodyPart.LeftLeg && part != AvatarMaskBodyPart.RightLeg
                      && part != AvatarMaskBodyPart.LeftFootIK && part != AvatarMaskBodyPart.RightFootIK;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    // ---- 4. content ----

    private void CreateContent()
    {
        EnsureFolder(ContentFolder);
        if (AssetDatabase.LoadAssetAtPath<EnemyBehaviourProfileSO>(BehaviourPath) == null)
        {
            var p = CreateInstance<EnemyBehaviourProfileSO>();
            p.melee = true;
            p.ranged = true;
            p.search = true;
            p.idle = true;
            AssetDatabase.CreateAsset(p, BehaviourPath);
            report.Add("Created " + BehaviourPath);
        }
        else report.Add("Kept existing " + BehaviourPath);

        string projPath = ContentFolder + "/Projectile_EnemyBolt.asset";
        var proj = AssetDatabase.LoadAssetAtPath<ProjectileSO>(projPath);
        if (proj == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<ProjectileSO>(SourceProjectile);
            proj = CreateInstance<ProjectileSO>();
            proj.id = "projectile_enemy_bolt";
            proj.displayName = "Enemy Bolt";
            proj.prefab = source != null ? source.prefab : null;
            proj.config = new ProjectileConfig
            {
                speed = 40f,
                maxLifetime = 3f,
                maxDistance = 60f,
                impactMode = ProjectileImpactMode.DestroyOnHit,
                stopOnEnvironment = true,
                collisionMask = LayerMask.GetMask("Default", "Ground", "Player")   // no Enemy layer: no friendly fire
            };
            AssetDatabase.CreateAsset(proj, projPath);
            report.Add("Created " + projPath + (proj.prefab == null ? " (? no prefab found: assign one)" : ""));
        }

        string delPath = ContentFolder + "/Delivery_EnemyBolt.asset";
        if (AssetDatabase.LoadAssetAtPath<ProjectileDeliverySO>(delPath) == null)
        {
            var del = CreateInstance<ProjectileDeliverySO>();
            del.projectile = proj;
            AssetDatabase.CreateAsset(del, delPath);
            report.Add("Created " + delPath);
        }
        AssetDatabase.SaveAssets();
    }

    // ---- 5. enemy ----

    private void Validate()
    {
        var go = enemy;
        var issues = new List<string>();
        var def = go.GetComponent<EnemyDefinitionApplier>();
        var profile = def != null ? def.BehaviourProfile : null;
        if (profile == null)
        {
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) profile = new SerializedObject(brain).FindProperty("defaultProfile").objectReferenceValue as EnemyBehaviourProfileSO;
        }
        if (go.GetComponent<EnemyBrain>() == null) issues.Add("No EnemyBrain");
        if (profile == null) issues.Add("No behaviour profile (definition or brain default)");
        EnemyBrain.CollectIssues(go, profile, issues);

        var anim = go.GetComponentInChildren<Animator>();
        if (anim == null) issues.Add("No Animator in children");
        else
        {
            if (anim.avatar == null || !anim.avatar.isHuman) issues.Add("Animator avatar is not Humanoid");
            if (anim.applyRootMotion) issues.Add("Animator Apply Root Motion is on (the NavMeshAgent moves the body)");
            if (anim.runtimeAnimatorController is AnimatorController ac)
            {
                foreach (var n in new[] { "MoveX", "MoveZ", "Aiming", "Fire", "Reload", "ReloadSpeed" })
                    if (ac.parameters.All(p => p.name != n)) issues.Add($"Animator: no '{n}' parameter");
                if (ac.layers.Length > 0 && !ac.layers[0].iKPass) issues.Add("Animator base layer: IK Pass off (look-at won't work)");
            }
        }

        if (issues.Count == 0) report.Add("No problems found.");
        else foreach (var i in issues) report.Add("! " + i);
    }

    private void AddComponents()
    {
        string prefabPath = PrefabUtility.IsPartOfPrefabAsset(enemy) ? AssetDatabase.GetAssetPath(enemy) : null;
        var root = prefabPath != null ? PrefabUtility.LoadPrefabContents(prefabPath) : enemy;
        try
        {
            var anim = root.GetComponentInChildren<Animator>();
            if (anim == null) { report.Add("! No Animator in children: add the body first."); return; }

            var ranged = root.GetComponent<EnemyRangedAttack>();
            if (ranged == null) { ranged = Undo.AddComponent<EnemyRangedAttack>(root); report.Add("Added EnemyRangedAttack"); }
            if (anim.GetComponent<EnemyShooterAnimator>() == null) { Undo.AddComponent<EnemyShooterAnimator>(anim.gameObject); report.Add("Added EnemyShooterAnimator"); }

            var so = new SerializedObject(ranged);
            var keys = AssetDatabase.LoadAssetAtPath<EnemyRangedStatKeys>(KeysPath);
            if (so.FindProperty("statKeys").objectReferenceValue == null && keys != null) so.FindProperty("statKeys").objectReferenceValue = keys;
            var del = AssetDatabase.LoadAssetAtPath<ProjectileDeliverySO>(ContentFolder + "/Delivery_EnemyBolt.asset");
            if (so.FindProperty("delivery").objectReferenceValue == null && del != null) so.FindProperty("delivery").objectReferenceValue = del;

            var model = anim.GetComponent<EnemyWeaponModel>();
            if (model == null) { model = Undo.AddComponent<EnemyWeaponModel>(anim.gameObject); report.Add("Added EnemyWeaponModel"); }
            var mso = new SerializedObject(model);
            if (weapon != null)
            {
                mso.FindProperty("weapon").objectReferenceValue = weapon;
                mso.ApplyModifiedProperties();
                report.Add("Weapon: " + weapon.name);
            }
            else if (mso.FindProperty("weapon").objectReferenceValue == null) report.Add("? No weapon: set one in this window or on EnemyWeaponModel.");

            // The gun's own muzzle replaces the loose one earlier runs created
            var loose = so.FindProperty("muzzle").objectReferenceValue as Transform;
            if (loose != null && loose.name == "Muzzle" && loose.childCount == 0 && loose.GetComponents<Component>().Length == 1)
            {
                so.FindProperty("muzzle").objectReferenceValue = null;
                if (PrefabUtility.IsPartOfPrefabInstance(loose)) report.Add("? Cleared the loose Muzzle; delete it from its prefab by hand.");
                else { Undo.DestroyObjectImmediate(loose.gameObject); report.Add("Removed the loose Muzzle: the gun's muzzle is used."); }
            }
            so.ApplyModifiedProperties();

            if (assignController)
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (ctrl != null) { Undo.RecordObject(anim, "Assign Controller"); anim.runtimeAnimatorController = ctrl; report.Add("Assigned " + ControllerPath); }
                else report.Add("? Shooter controller not built yet.");
            }

            if (prefabPath != null) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            else EditorUtility.SetDirty(root);
        }
        finally
        {
            if (prefabPath != null) PrefabUtility.UnloadPrefabContents(root);
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
