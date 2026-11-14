using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Skinless Mixamo clips: Humanoid from their own skeleton, with a reference model's T-pose as the rest pose
public class ClipAvatarFixer : EditorWindow
{
    private const string DefaultReference = "Assets/Animations/Player/ThirdPerson Weapon Packs/CharacterModel/Ch15_nonPBR.fbx";

    [SerializeField] private GameObject reference;
    [SerializeField] private List<Object> targets = new List<Object>();
    [SerializeField] private bool copyPositions;
    [SerializeField] private bool clipSettings = true;
    [SerializeField] private bool renameClips = true;
    [SerializeField] private bool feetOnGround;
    private readonly List<string> report = new List<string>();
    private Vector2 scroll;

    [MenuItem("Tools/Fix Clip Avatars")]
    private static void Open() => GetWindow<ClipAvatarFixer>("Fix Clip Avatars");

    // Opens with these files or folders listed
    public static void Open(params Object[] items)
    {
        var w = GetWindow<ClipAvatarFixer>("Fix Clip Avatars");
        foreach (var o in items)
            if (o != null && !w.targets.Contains(o)) w.targets.Add(o);
    }

    private void OnEnable()
    {
        if (reference == null) reference = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultReference);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        reference = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Reference Model",
            "A Humanoid model in T-pose on a Mixamo skeleton (a character downloaded with skin)."), reference, typeof(GameObject), false);
        var refAvatar = RefAvatar();
        if (reference != null && refAvatar == null) EditorGUILayout.HelpBox("The reference has no Humanoid avatar.", MessageType.Error);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clip files or folders", EditorStyles.boldLabel);
        for (int i = 0; i < targets.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            targets[i] = EditorGUILayout.ObjectField(targets[i], typeof(Object), false);
            if (GUILayout.Button("-", GUILayout.Width(24))) { targets.RemoveAt(i); i--; }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add")) targets.Add(null);
        if (GUILayout.Button("Add Selection"))
            foreach (var o in Selection.objects)
                if (!targets.Contains(o)) targets.Add(o);
        if (GUILayout.Button("Clear")) targets.Clear();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        copyPositions = EditorGUILayout.Toggle(new GUIContent("Copy Bone Positions",
            "Off: rest rotations only (safe across bodies). On: lengths too; only when the clip was made on the reference's skeleton."), copyPositions);
        clipSettings = EditorGUILayout.Toggle(new GUIContent("Mixamo Clip Settings",
            "Bake root rotation and Y, leave XZ unbaked, loop by name (hit, death, jump and turn clips don't loop)."), clipSettings);
        renameClips = EditorGUILayout.Toggle(new GUIContent("Name Clip After File", "Replaces the 'mixamo.com' take name."), renameClips);
        feetOnGround = EditorGUILayout.Toggle(new GUIContent("Feet On Ground",
            "Root height based on the feet, not the clip's hips: keeps a foot planted when the clip was made on a body with other proportions."), feetOnGround);

        var paths = ModelPaths().ToList();
        GUI.enabled = refAvatar != null && paths.Count > 0;
        if (GUILayout.Button($"Fix {paths.Count} Clip File(s)", GUILayout.Height(28))) Fix(paths, refAvatar);
        GUI.enabled = true;

        foreach (var line in report)
        {
            var type = line.StartsWith("!") ? MessageType.Error : line.StartsWith("?") ? MessageType.Warning : MessageType.None;
            EditorGUILayout.HelpBox(line.TrimStart('!', '?', ' '), type);
        }
        EditorGUILayout.EndScrollView();
    }

    private Avatar RefAvatar()
    {
        if (reference == null) return null;
        var avatar = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(reference)).OfType<Avatar>().FirstOrDefault();
        return avatar != null && avatar.isHuman ? avatar : null;
    }

    private void Fix(List<string> paths, Avatar refAvatar)
    {
        report.Clear();
        string refPath = AssetDatabase.GetAssetPath(reference);
        var refBones = new Dictionary<string, SkeletonBone>();
        var refSkeleton = refAvatar.humanDescription.skeleton;
        for (int i = 1; i < refSkeleton.Length; i++) refBones[refSkeleton[i].name] = refSkeleton[i];

        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (paths[i] == refPath) continue;
                EditorUtility.DisplayProgressBar("Fix Clip Avatars", paths[i], (float)i / paths.Count);
                FixOne(paths[i], refBones);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        report.Insert(0, $"Done: {paths.Count} file(s). Re-run anything that copied these clips (hold poses, controllers).");
    }

    private void FixOne(string path, Dictionary<string, SkeletonBone> refBones)
    {
        string file = Path.GetFileNameWithoutExtension(path);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        // Humanoid from its own skeleton first, so Unity maps the bones and records the hierarchy
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar = null;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isHuman) { report.Add($"! {file}: no Humanoid avatar after import (not a Mixamo skeleton?)"); return; }

        var hd = avatar.humanDescription;
        var skeleton = hd.skeleton;
        int matched = 0;
        float worst = 0f;
        string worstBone = null;
        for (int i = 1; i < skeleton.Length; i++)
        {
            if (!refBones.TryGetValue(skeleton[i].name, out var rb)) continue;
            matched++;
            // Bone length check, skipping the hips (offset from the root, not a length)
            if (i > 1 && !skeleton[i].name.EndsWith("Hips"))
            {
                float a = skeleton[i].position.magnitude, b = rb.position.magnitude;
                if (a > 0.0001f && b > 0.0001f && Mathf.Abs(a / b - 1f) > worst) { worst = Mathf.Abs(a / b - 1f); worstBone = skeleton[i].name; }
            }
            skeleton[i].rotation = rb.rotation;
            if (copyPositions) { skeleton[i].position = rb.position; skeleton[i].scale = rb.scale; }
        }
        if (matched == 0) { report.Add($"! {file}: no bone names match the reference"); return; }

        hd.skeleton = skeleton;
        importer.humanDescription = hd;

        if (clipSettings || renameClips || feetOnGround)
        {
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            bool loop = !MixamoClipSetup.OneShot.Any(file.ToLowerInvariant().Contains);
            foreach (var c in clips)
            {
                if (clipSettings) MixamoClipSetup.Apply(c, loop);
                if (renameClips && clips.Length == 1) c.name = file;
                if (feetOnGround) { c.lockRootHeightY = true; c.keepOriginalPositionY = false; c.heightFromFeet = true; }
            }
            importer.clipAnimations = clips;
        }
        importer.SaveAndReimport();

        string line = $"{file}: {matched} bones set to the reference pose";
        if (clipSettings) line += $", loop {(MixamoClipSetup.OneShot.Any(file.ToLowerInvariant().Contains) ? "off" : "on")}";
        if (worst > 0.1f && copyPositions)
            line = "? " + line + $". Bone lengths differ from the reference (up to {worst:P0} on {worstBone}): turn Copy Bone Positions off and fix again.";
        report.Add(line);
    }

    // Model files listed, plus every model inside listed folders
    private IEnumerable<string> ModelPaths()
    {
        var paths = new HashSet<string>();
        foreach (var o in targets)
        {
            if (o == null) continue;
            string path = AssetDatabase.GetAssetPath(o);
            if (string.IsNullOrEmpty(path)) continue;
            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { path }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            else if (AssetImporter.GetAtPath(path) is ModelImporter) paths.Add(path);
        }
        return paths.OrderBy(p => p);
    }
}
