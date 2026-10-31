using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds Attack_0..N states on an enemy controller, driven by Attacking (bool) + AttackIndex (int) + AttackSpeed (float).
public class EnemyAttackAnimatorSetup : EditorWindow
{
    private AnimatorController controller;
    private int layerIndex;
    private int returnStateIndex;   // 0 = layer default state
    private readonly List<Object> clips = new List<Object>();
    private string statePrefix = "Attack";
    private string attackingParam = "Attacking";
    private string indexParam = "AttackIndex";
    private string speedParam = "AttackSpeed";
    private float inDuration = 0.1f;
    private float outDuration = 0.2f;
    private bool fixImport = true;
    private Vector2 scroll;

    [MenuItem("Tools/Enemy Attack Animator Setup")]
    private static void Open() => GetWindow<EnemyAttackAnimatorSetup>("Enemy Attack Setup");

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUI.BeginChangeCheck();
        controller = (AnimatorController)EditorGUILayout.ObjectField("Controller", controller, typeof(AnimatorController), false);
        if (EditorGUI.EndChangeCheck()) { layerIndex = 0; returnStateIndex = 0; if (controller != null) ReadExisting(); }

        if (controller == null)
        {
            EditorGUILayout.HelpBox("Assign the enemy's Animator Controller.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        var layers = controller.layers;
        layerIndex = Mathf.Clamp(EditorGUILayout.Popup("Layer", layerIndex, layers.Select(l => l.name).ToArray()), 0, layers.Length - 1);

        var states = layers[layerIndex].stateMachine.states.Select(s => s.state)
            .Where(s => !s.name.StartsWith(statePrefix + "_")).ToList();
        var names = new List<string> { "(layer default)" };
        names.AddRange(states.Select(s => s.name));
        returnStateIndex = Mathf.Clamp(EditorGUILayout.Popup("Return State", returnStateIndex, names.ToArray()), 0, names.Count - 1);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Attack Clips (list index = AttackIndex = EnemyMeleeAttack move index)", EditorStyles.boldLabel);
        for (int i = 0; i < clips.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            clips[i] = EditorGUILayout.ObjectField($"{statePrefix}_{i}", clips[i], typeof(Object), false);
            if (GUILayout.Button("-", GUILayout.Width(24))) { clips.RemoveAt(i); i--; }
            EditorGUILayout.EndHorizontal();
            if (i >= 0 && i < clips.Count && clips[i] != null && ResolveClip(clips[i]) == null)
                EditorGUILayout.HelpBox("No animation clip found in that asset.", MessageType.Warning);
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Clip")) clips.Add(null);
        if (GUILayout.Button("Read Existing")) ReadExisting();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Names & Blends", EditorStyles.boldLabel);
        statePrefix = EditorGUILayout.TextField("State Prefix", statePrefix);
        attackingParam = EditorGUILayout.TextField("Attacking (bool)", attackingParam);
        indexParam = EditorGUILayout.TextField("Attack Index (int)", indexParam);
        speedParam = EditorGUILayout.TextField("Attack Speed (float)", speedParam);
        inDuration = EditorGUILayout.FloatField("Blend In (s)", inDuration);
        outDuration = EditorGUILayout.FloatField("Blend Out (s)", outDuration);
        fixImport = EditorGUILayout.Toggle(new GUIContent("Fix Clip Import", "Bake root rotation / Y / XZ into pose, Loop Time off."), fixImport);

        EditorGUILayout.Space();
        GUI.enabled = clips.Count > 0 && clips.All(c => c != null && ResolveClip(c) != null);
        if (GUILayout.Button("Apply", GUILayout.Height(28))) Apply(states);
        GUI.enabled = true;

        EditorGUILayout.HelpBox("Re-running updates the same states and transitions. Extra " + statePrefix +
            "_N states beyond the list are removed.", MessageType.None);
        EditorGUILayout.EndScrollView();
    }

    // Fill the list from Attack_N states already on the layer.
    private void ReadExisting()
    {
        clips.Clear();
        if (controller == null) return;
        var sm = controller.layers[Mathf.Clamp(layerIndex, 0, controller.layers.Length - 1)].stateMachine;
        for (int i = 0; ; i++)
        {
            var st = FindState(sm, $"{statePrefix}_{i}");
            if (st == null) break;
            clips.Add(st.motion);
        }
    }

    private void Apply(List<AnimatorState> candidates)
    {
        var resolved = new List<AnimationClip>();
        for (int i = 0; i < clips.Count; i++)
        {
            var src = ResolveClip(clips[i]);
            string path = AssetDatabase.GetAssetPath(src);
            string clipName = src.name;
            if (fixImport) FixImport(src);

            // Reimport can replace the clip object, so reload it by path and name.
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => x.name == clipName);
            if (clip == null) { Debug.LogError($"[AttackSetup] Clip {i} missing after import."); return; }
            clips[i] = clip;
            resolved.Add(clip);
        }

        var sm = controller.layers[layerIndex].stateMachine;
        var ret = returnStateIndex == 0 ? sm.defaultState : candidates[returnStateIndex - 1];
        if (ret == null) { Debug.LogError("[AttackSetup] Layer has no default state; pick a Return State."); return; }

        Undo.RegisterCompleteObjectUndo(new Object[] { controller, sm }, "Enemy Attack Setup");

        EnsureParam(attackingParam, AnimatorControllerParameterType.Bool, 0f);
        EnsureParam(indexParam, AnimatorControllerParameterType.Int, 0f);
        EnsureParam(speedParam, AnimatorControllerParameterType.Float, 1f);

        // Remove stale Attack_N states past the list.
        for (int i = resolved.Count; ; i++)
        {
            var stale = FindState(sm, $"{statePrefix}_{i}");
            if (stale == null) break;
            sm.RemoveState(stale);
        }

        Vector3 basePos = PositionOf(sm, ret) + new Vector3(300f, 0f, 0f);
        for (int i = 0; i < resolved.Count; i++)
        {
            string name = $"{statePrefix}_{i}";
            var st = FindState(sm, name) ?? sm.AddState(name, basePos + new Vector3(0f, i * 60f, 0f));

            st.motion = resolved[i];
            st.speedParameterActive = true;
            st.speedParameter = speedParam;

            RemoveTransitions(ret, st);
            RemoveTransitions(st, ret);

            var tin = ret.AddTransition(st);
            tin.hasExitTime = false;
            tin.hasFixedDuration = true;
            tin.duration = inDuration;
            tin.AddCondition(AnimatorConditionMode.If, 0f, attackingParam);
            tin.AddCondition(AnimatorConditionMode.Equals, i, indexParam);

            var tout = st.AddTransition(ret);
            tout.hasExitTime = false;
            tout.hasFixedDuration = true;
            tout.duration = outDuration;
            tout.AddCondition(AnimatorConditionMode.IfNot, 0f, attackingParam);
        }

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(sm);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AttackSetup] {resolved.Count} attack state(s) on '{controller.name}' layer '{controller.layers[layerIndex].name}', returning to '{ret.name}'.");
    }

    private void EnsureParam(string name, AnimatorControllerParameterType type, float def)
    {
        var ps = controller.parameters;
        int idx = System.Array.FindIndex(ps, p => p.name == name);
        if (idx >= 0 && ps[idx].type != type)
        {
            controller.RemoveParameter(idx);
            idx = -1;
        }
        if (idx < 0)
        {
            controller.AddParameter(name, type);
            ps = controller.parameters;
            idx = ps.Length - 1;
            ps[idx].defaultFloat = def;
            ps[idx].defaultInt = (int)def;
            controller.parameters = ps;
        }
    }

    private static void RemoveTransitions(AnimatorState from, AnimatorState to)
    {
        foreach (var t in from.transitions.Where(t => t.destinationState == to).ToArray())
            from.RemoveTransition(t);
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (var s in sm.states) if (s.state.name == name) return s.state;
        return null;
    }

    private static Vector3 PositionOf(AnimatorStateMachine sm, AnimatorState state)
    {
        foreach (var s in sm.states) if (s.state == state) return s.position;
        return new Vector3(300f, 100f, 0f);
    }

    // Accepts a clip, or a model file holding one.
    private static AnimationClip ResolveClip(Object o)
    {
        if (o is AnimationClip c) return c;
        if (o == null) return null;
        string path = AssetDatabase.GetAssetPath(o);
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(a => !a.name.StartsWith("__preview__"));
    }

    // Bake root motion into pose and turn looping off on the clip's model importer.
    private static void FixImport(AnimationClip clip)
    {
        if (clip == null) return;
        string path = AssetDatabase.GetAssetPath(clip);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        var anims = importer.clipAnimations;
        if (anims == null || anims.Length == 0) anims = importer.defaultClipAnimations;

        bool changed = false;
        foreach (var a in anims)
        {
            if (a.name != clip.name && a.takeName != clip.name) continue;
            if (a.lockRootRotation && a.lockRootHeightY && a.lockRootPositionXZ && !a.loopTime) continue;
            a.lockRootRotation = true;
            a.lockRootHeightY = true;
            a.lockRootPositionXZ = true;
            a.loopTime = false;
            changed = true;
        }
        if (!changed) return;

        importer.clipAnimations = anims;
        importer.SaveAndReimport();
        Debug.Log($"[AttackSetup] Baked root motion into pose for '{clip.name}'.");
    }
}
