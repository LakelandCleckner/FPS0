using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Core;

// Builds trigger hitboxes on the third-person body's humanoid bones:
// capsules sized from bone lengths, or convex hulls cut from the skinned mesh per bone
public class PlayerHitboxSetup : EditorWindow
{
    private const string Prefix = "HB_";

    private enum Shape { Capsules, MeshFit }

    [SerializeField] private Animator body;
    [SerializeField] private string layerName = "Player";
    [SerializeField] private Shape shape = Shape.MeshFit;
    [SerializeField] private string meshFolder = "Assets/Animations/Player/ThirdPerson Weapon Packs/TP_Generated/Hitboxes";

    [SerializeField] private float headRadius = 0.12f;
    [SerializeField] private float torsoRadius = 0.16f;
    [SerializeField] private float upperArmRadius = 0.06f;
    [SerializeField] private float lowerArmRadius = 0.05f;
    [SerializeField] private float upperLegRadius = 0.09f;
    [SerializeField] private float lowerLegRadius = 0.065f;
    [SerializeField] private float footRadius = 0.05f;

    [SerializeField] private float headMultiplier = 1f;
    [SerializeField] private float torsoMultiplier = 1f;
    [SerializeField] private float armMultiplier = 1f;
    [SerializeField] private float legMultiplier = 1f;

    private Vector2 scroll;

    [MenuItem("Tools/Player Hitbox Setup")]
    private static void Open() => GetWindow<PlayerHitboxSetup>("Player Hitbox Setup");

    private void OnEnable()
    {
        if (body != null) return;
        var vis = FindFirstObjectByType<BodyVisibility>();
        if (vis != null) body = vis.GetComponent<Animator>();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        var so = new SerializedObject(this);
        EditorGUILayout.PropertyField(so.FindProperty("body"), new GUIContent("Body (scene)"));
        EditorGUILayout.PropertyField(so.FindProperty("layerName"));
        EditorGUILayout.PropertyField(so.FindProperty("shape"));

        if (shape == Shape.MeshFit)
        {
            EditorGUILayout.PropertyField(so.FindProperty("meshFolder"));
        }
        else
        {
            EditorGUILayout.LabelField("Radius (m)", EditorStyles.boldLabel);
            foreach (var p in new[] { "headRadius", "torsoRadius", "upperArmRadius", "lowerArmRadius",
                                      "upperLegRadius", "lowerLegRadius", "footRadius" })
                EditorGUILayout.PropertyField(so.FindProperty(p));
        }

        EditorGUILayout.LabelField("Damage multiplier", EditorStyles.boldLabel);
        foreach (var p in new[] { "headMultiplier", "torsoMultiplier", "armMultiplier", "legMultiplier" })
            EditorGUILayout.PropertyField(so.FindProperty(p));
        so.ApplyModifiedProperties();

        string problem = Problem();
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(problem != null))
        {
            if (GUILayout.Button("Build Hitboxes", GUILayout.Height(28))) Build();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Hitboxes")) SelectAll();
            if (GUILayout.Button("Remove Hitboxes")) Remove();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.HelpBox("Build outside Play mode. Rebuilding replaces every HB_ object. " +
                                "Mesh Fit cuts the full-body mesh (not Legs) into one convex hull per bone group " +
                                "and saves the meshes to Mesh Folder. Select Hitboxes shows them as collider gizmos.",
                                MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    private string Problem()
    {
        if (Application.isPlaying) return "Exit Play mode first.";
        if (body == null) return "Assign the body (the Ch15 in the scene).";
        if (!body.isHuman) return "Body Animator needs a humanoid avatar.";
        if (LayerMask.NameToLayer(layerName) < 0) return $"No layer named '{layerName}'.";
        if (body.GetComponentInParent<PlayerHurtbox>() == null) return "No PlayerHurtbox above the body.";
        if (shape == Shape.MeshFit && !meshFolder.StartsWith("Assets")) return "Mesh Folder must be under Assets.";
        return null;
    }

    private void Build()
    {
        Undo.SetCurrentGroupName("Build Player Hitboxes");
        int group = Undo.GetCurrentGroup();
        Remove();

        int layer = LayerMask.NameToLayer(layerName);
        var made = new List<string>();
        if (shape == Shape.MeshFit) BuildMeshFit(made, layer);
        else BuildCapsules(made, layer);

        Undo.CollapseUndoOperations(group);
        Debug.Log($"[PlayerHitboxSetup] Built {made.Count} hitboxes on '{body.name}': {string.Join(", ", made)}.");
        SelectAll();
    }

    // ===== Mesh fit =====

    private sealed class Part
    {
        public Transform Anchor;
        public BodyPart BodyPart;
        public float Multiplier;
    }

    // Which part each humanoid bone's vertices belong to (descendants, e.g. fingers, inherit)
    private static readonly (HumanBodyBones bone, string key)[] BoneParts =
    {
        (HumanBodyBones.Hips, "TorsoPelvis"),
        (HumanBodyBones.Spine, "TorsoSpine"),
        (HumanBodyBones.Chest, "TorsoChest"),
        (HumanBodyBones.UpperChest, "TorsoUpper"),
        (HumanBodyBones.Neck, "TorsoUpper"),
        (HumanBodyBones.LeftShoulder, "TorsoUpper"),
        (HumanBodyBones.RightShoulder, "TorsoUpper"),
        (HumanBodyBones.Head, "Head"),
        (HumanBodyBones.LeftUpperArm, "UpperArm_L"),
        (HumanBodyBones.LeftLowerArm, "LowerArm_L"),
        (HumanBodyBones.LeftHand, "Hand_L"),
        (HumanBodyBones.RightUpperArm, "UpperArm_R"),
        (HumanBodyBones.RightLowerArm, "LowerArm_R"),
        (HumanBodyBones.RightHand, "Hand_R"),
        (HumanBodyBones.LeftUpperLeg, "UpperLeg_L"),
        (HumanBodyBones.LeftLowerLeg, "LowerLeg_L"),
        (HumanBodyBones.LeftFoot, "Foot_L"),
        (HumanBodyBones.LeftToes, "Foot_L"),
        (HumanBodyBones.RightUpperLeg, "UpperLeg_R"),
        (HumanBodyBones.RightLowerLeg, "LowerLeg_R"),
        (HumanBodyBones.RightFoot, "Foot_R"),
        (HumanBodyBones.RightToes, "Foot_R"),
    };

    private static Transform First(params Transform[] options)
    {
        foreach (var t in options) if (t != null) return t;
        return null;
    }

    private Dictionary<string, Part> MakeParts()
    {
        Transform B(HumanBodyBones b) => body.GetBoneTransform(b);
        var spine = B(HumanBodyBones.Spine);
        var chest = First(B(HumanBodyBones.Chest), spine);
        var upper = First(B(HumanBodyBones.UpperChest), chest);

        var parts = new Dictionary<string, Part>();
        void Add(string key, Transform anchor, BodyPart part, float mult)
        {
            if (anchor != null) parts[key] = new Part { Anchor = anchor, BodyPart = part, Multiplier = mult };
        }

        Add("TorsoPelvis", B(HumanBodyBones.Hips), BodyPart.Torso, torsoMultiplier);
        Add("TorsoSpine", spine, BodyPart.Torso, torsoMultiplier);
        Add("TorsoChest", chest, BodyPart.Torso, torsoMultiplier);
        Add("TorsoUpper", upper, BodyPart.Torso, torsoMultiplier);
        Add("Head", B(HumanBodyBones.Head), BodyPart.Head, headMultiplier);
        foreach (var s in new[] { "L", "R" })
        {
            bool l = s == "L";
            Add($"UpperArm_{s}", B(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm), BodyPart.Arm, armMultiplier);
            Add($"LowerArm_{s}", B(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm), BodyPart.Arm, armMultiplier);
            Add($"Hand_{s}", B(l ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand), BodyPart.Arm, armMultiplier);
            Add($"UpperLeg_{s}", B(l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg), BodyPart.Leg, legMultiplier);
            Add($"LowerLeg_{s}", B(l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg), BodyPart.Leg, legMultiplier);
            Add($"Foot_{s}", B(l ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot), BodyPart.Leg, legMultiplier);
        }
        return parts;
    }

    private void BuildMeshFit(List<string> made, int layer)
    {
        var parts = MakeParts();

        // Bone -> anchor, and anchor -> the first part key that uses it (anchors can be shared when bones are missing)
        var ownerToAnchor = new Dictionary<Transform, Transform>();
        foreach (var (bone, key) in BoneParts)
        {
            var t = body.GetBoneTransform(bone);
            if (t != null && !ownerToAnchor.ContainsKey(t) && parts.TryGetValue(key, out var p)) ownerToAnchor[t] = p.Anchor;
        }
        var anchorKey = new Dictionary<Transform, string>();
        foreach (var kv in parts)
            if (!anchorKey.ContainsKey(kv.Value.Anchor)) anchorKey[kv.Value.Anchor] = kv.Key;

        // Full body only; the legs-only mesh duplicates it
        var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                            .Where(r => !r.name.ToLowerInvariant().Contains("legs"));
        var pieces = HitboxHullCutter.Cut(renderers, ownerToAnchor, body.transform);
        if (pieces.Count == 0)
        {
            Debug.LogError("[PlayerHitboxSetup] No usable skinned mesh found under the body.");
            return;
        }

        foreach (var kv in pieces)
        {
            if (!anchorKey.TryGetValue(kv.Key, out var key)) continue;
            var p = parts[key];
            var mesh = HitboxHullCutter.SaveHullMesh(kv.Value, meshFolder, Prefix + key);
            if (mesh == null) continue;

            var go = Make(key, p.Anchor, layer, p.BodyPart, p.Multiplier);
            HitboxHullCutter.AddHull(go, mesh, true);
            made.Add(go.name);
        }
        AssetDatabase.SaveAssets();
    }

    // ===== Capsules =====

    private void BuildCapsules(List<string> made, int layer)
    {
        Transform B(HumanBodyBones b) => body.GetBoneTransform(b);

        // Head: sphere above the head bone
        var head = B(HumanBodyBones.Head);
        if (head != null)
        {
            var go = Make("Head", head, layer, BodyPart.Head, headMultiplier);
            var s = go.AddComponent<SphereCollider>();
            s.isTrigger = true;
            s.radius = headRadius;
            go.transform.position = head.position + body.transform.up * headRadius * 0.8f;
            made.Add(go.name);
        }

        // Torso: hips to chest, chest to neck, so the spine bend carries through
        var hips = B(HumanBodyBones.Hips);
        var chest = First(B(HumanBodyBones.Chest), B(HumanBodyBones.Spine));
        var neck = First(B(HumanBodyBones.Neck), head);
        Segment(made, "TorsoLower", hips, chest, torsoRadius, BodyPart.Torso, torsoMultiplier, layer, 1f);
        Segment(made, "TorsoUpper", chest, neck, torsoRadius, BodyPart.Torso, torsoMultiplier, layer, 1f);

        foreach (var side in new[] { "L", "R" })
        {
            bool l = side == "L";
            var upperArm = B(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lowerArm = B(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = B(l ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var upperLeg = B(l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var lowerLeg = B(l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var foot = B(l ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            var toes = B(l ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);

            Segment(made, $"UpperArm_{side}", upperArm, lowerArm, upperArmRadius, BodyPart.Arm, armMultiplier, layer, 1f);
            Segment(made, $"LowerArm_{side}", lowerArm, hand, lowerArmRadius, BodyPart.Arm, armMultiplier, layer, 1.35f);
            Segment(made, $"UpperLeg_{side}", upperLeg, lowerLeg, upperLegRadius, BodyPart.Leg, legMultiplier, layer, 1f);
            Segment(made, $"LowerLeg_{side}", lowerLeg, foot, lowerLegRadius, BodyPart.Leg, legMultiplier, layer, 1f);
            if (toes != null) Segment(made, $"Foot_{side}", foot, toes, footRadius, BodyPart.Leg, legMultiplier, layer, 1.4f);
        }
    }

    // Capsule from bone a toward bone b, parented to a; lengthScale extends past b (hands, toes)
    private void Segment(List<string> made, string name, Transform a, Transform b, float radius,
                         BodyPart part, float multiplier, int layer, float lengthScale)
    {
        if (a == null || b == null) return;
        Vector3 dir = b.position - a.position;
        float length = dir.magnitude * lengthScale;
        if (length < 0.01f) return;

        var go = Make(name, a, layer, part, multiplier);
        go.transform.SetPositionAndRotation(a.position + dir.normalized * length * 0.5f,
                                            Quaternion.FromToRotation(Vector3.up, dir));

        var c = go.AddComponent<CapsuleCollider>();
        c.isTrigger = true;
        c.direction = 1;
        c.radius = radius;
        c.height = length + radius;   // ends just past each joint
        made.Add(go.name);
    }

    // ===== Shared =====

    // Child of a bone; local scale cancels the bone's scale so capsule sizes are in metres
    private GameObject Make(string name, Transform bone, int layer, BodyPart part, float multiplier)
    {
        var go = new GameObject(Prefix + name);
        Undo.RegisterCreatedObjectUndo(go, "Build Player Hitboxes");
        go.layer = layer;
        go.transform.SetParent(bone, false);
        Vector3 s = bone.lossyScale;
        go.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

        var hb = go.AddComponent<PlayerHitbox>();
        hb.bodyPart = part;
        hb.damageMultiplier = multiplier;
        return go;
    }

    private GameObject[] Existing() =>
        body == null ? new GameObject[0]
                     : body.GetComponentsInChildren<PlayerHitbox>(true).Select(h => h.gameObject).ToArray();

    private void Remove()
    {
        foreach (var go in Existing()) Undo.DestroyObjectImmediate(go);
    }

    private void SelectAll()
    {
        var all = Existing().Cast<Object>().ToArray();
        if (all.Length > 0) Selection.objects = all;
    }
}
