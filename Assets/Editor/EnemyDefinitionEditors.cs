using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Combat.Enemies;
using Combat.Stats;
using Combat.Zones;

// Inspector preview, final stats and checks for enemy archetypes, modifiers and variants.
public class EnemyPreview
{
    private PreviewRenderUtility util;
    private GameObject source;
    private GameObject instance;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private Vector3 baseScale;
    private Bounds bounds;
    private Vector2 orbit = new Vector2(150f, 8f);
    private float zoom = 1f;

    public bool ShowPlain;

    public void Draw(GameObject prefab, EnemyLook look, float height)
    {
        Rect rect = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true));
        if (prefab == null) { EditorGUI.HelpBox(rect, "No body prefab to preview.", MessageType.Info); return; }

        Ensure(prefab);
        HandleInput(rect);
        if (Event.current.type != EventType.Repaint) return;

        var l = ShowPlain ? EnemyLook.Plain : look;
        instance.transform.localScale = baseScale * l.Scale;
        l.ApplyTo(renderers, block);

        float radius = Mathf.Max(0.1f, bounds.extents.magnitude);
        var cam = util.camera;
        float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * zoom;
        var rot = Quaternion.Euler(orbit.y, orbit.x, 0f);
        cam.transform.SetPositionAndRotation(bounds.center - rot * Vector3.forward * dist, rot);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = dist + radius * 4f;

        util.BeginPreview(rect, GUIStyle.none);
        cam.Render();
        util.EndAndDrawPreview(rect);
    }

    public void Cleanup()
    {
        if (util != null) util.Cleanup();
        util = null;
        instance = null;
        source = null;
    }

    private void Ensure(GameObject prefab)
    {
        if (util == null)
        {
            util = new PreviewRenderUtility();
            util.camera.fieldOfView = 30f;
            util.camera.clearFlags = CameraClearFlags.SolidColor;
            util.camera.backgroundColor = new Color(0.17f, 0.18f, 0.2f, 1f);
            util.lights[0].intensity = 1.1f;
            util.lights[0].transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            util.lights[1].intensity = 0.6f;
            util.ambientColor = new Color(0.35f, 0.35f, 0.38f, 1f);
            block = new MaterialPropertyBlock();
            source = null;
        }
        if (source == prefab && instance != null) return;

        if (instance != null) Object.DestroyImmediate(instance);
        source = prefab;
        instance = util.InstantiatePrefabInScene(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        baseScale = instance.transform.localScale;
        renderers = EnemyLook.FindRenderers(instance);

        bounds = new Bounds(Vector3.up, Vector3.one);
        bool first = true;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }
    }

    // Drag to orbit, scroll to zoom.
    private void HandleInput(Rect rect)
    {
        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    orbit.x += e.delta.x * 0.6f;
                    orbit.y = Mathf.Clamp(orbit.y + e.delta.y * 0.6f, -60f, 60f);
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
            case EventType.ScrollWheel:
                if (rect.Contains(e.mousePosition)) { zoom = Mathf.Clamp(zoom * (1f + e.delta.y * 0.05f), 0.3f, 3f); e.Use(); }
                break;
        }
    }
}

// Final stat math, summary and checks for an archetype plus modifier layers.
public static class EnemyDefinitionReport
{
    public class Row
    {
        public StatDefinitionSO Stat;
        public float Base;
        public string BaseSource;
        public float Flat, Additive, Mult = 1f;
        public readonly StringBuilder Changes = new StringBuilder();
        public float Final;
    }

    public static List<Row> BuildStats(EnemyArchetypeSO arch, IReadOnlyList<EnemyModifierSO> layers, GameObject prefab)
    {
        var rows = new List<Row>();
        var byStat = new Dictionary<StatDefinitionSO, Row>();
        var prefabBases = PrefabBases(prefab);

        if (arch != null)
            foreach (var b in arch.baseStats)
            {
                if (b.stat == null) continue;
                var r = GetRow(b.stat, rows, byStat, prefabBases);
                r.Base = b.baseValue;
                r.BaseSource = "archetype";
            }

        if (layers != null)
            foreach (var m in layers)
            {
                if (m == null) continue;
                foreach (var e in m.stats)
                {
                    if (e.stat == null) continue;
                    var r = GetRow(e.stat, rows, byStat, prefabBases);
                    switch (e.op)
                    {
                        case EnemyStatOp.SetBase: r.Base = e.value; r.BaseSource = m.StatSourceLabel; break;
                        case EnemyStatOp.Flat: r.Flat += e.value; break;
                        case EnemyStatOp.Additive: r.Additive += e.value; break;
                        case EnemyStatOp.Multiply: r.Mult *= Mathf.Max(0f, e.value); break;
                    }
                    if (r.Changes.Length > 0) r.Changes.Append(", ");
                    r.Changes.Append(m.StatSourceLabel).Append(' ').Append(OpText(e.op, e.value));
                }
            }

        foreach (var r in rows)
        {
            float v = r.Stat.resolutionMode == StatResolutionMode.Pool
                ? Mathf.Max(0f, r.Base + r.Flat + r.Additive) * r.Mult
                : (r.Base + r.Flat) * Mathf.Max(0f, 1f + r.Additive) * r.Mult;
            r.Final = r.Stat.Clamp(v);
        }
        return rows;
    }

    private static Row GetRow(StatDefinitionSO stat, List<Row> rows, Dictionary<StatDefinitionSO, Row> byStat,
                              Dictionary<StatDefinitionSO, float> prefabBases)
    {
        if (byStat.TryGetValue(stat, out var r)) return r;
        r = new Row { Stat = stat };
        if (prefabBases.TryGetValue(stat, out float pb)) { r.Base = pb; r.BaseSource = "prefab"; }
        else { r.Base = stat.defaultValue; r.BaseSource = "default"; }
        byStat[stat] = r;
        rows.Add(r);
        return r;
    }

    public static string OpText(EnemyStatOp op, float v)
    {
        switch (op)
        {
            case EnemyStatOp.Flat: return (v >= 0f ? "+" : "") + v.ToString("0.###");
            case EnemyStatOp.Additive: return (v >= 0f ? "+" : "") + (v * 100f).ToString("0.#") + "%";
            case EnemyStatOp.Multiply: return "x" + v.ToString("0.###");
            default: return "= " + v.ToString("0.###");
        }
    }

    public static string Value(StatDefinitionSO stat, float v)
        => stat.isPercent ? (v * 100f).ToString("0.#") + "%" : v.ToString("0.###");

    public static string StatName(StatDefinitionSO s)
        => !string.IsNullOrEmpty(s.displayName) ? s.displayName : (!string.IsNullOrEmpty(s.id) ? s.id : s.name);

    // CombatantStats base values serialized on the prefab.
    private static Dictionary<StatDefinitionSO, float> PrefabBases(GameObject prefab)
    {
        var map = new Dictionary<StatDefinitionSO, float>();
        var cs = prefab != null ? prefab.GetComponent<CombatantStats>() : null;
        if (cs == null) return map;
        var arr = new SerializedObject(cs).FindProperty("baseStats");
        if (arr == null) return map;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            var stat = el.FindPropertyRelative("stat").objectReferenceValue as StatDefinitionSO;
            if (stat != null) map[stat] = el.FindPropertyRelative("baseValue").floatValue;
        }
        return map;
    }

    public class Issue
    {
        public string Text;
        public string FixLabel;
        public System.Action Fix;
    }

    private static Issue Note(List<Issue> list, string text, string fixLabel = null, System.Action fix = null)
    {
        var issue = new Issue { Text = text, FixLabel = fixLabel, Fix = fix };
        list.Add(issue);
        return issue;
    }

    public static List<Issue> Checks(EnemyArchetypeSO arch, IReadOnlyList<EnemyModifierSO> layers, GameObject prefab, EnemyLook look)
    {
        var list = new List<Issue>();
        if (arch == null) { Note(list, "No archetype."); return list; }
        if (string.IsNullOrEmpty(arch.id)) Note(list, $"Archetype '{arch.name}' has no id.");
        if (prefab == null) Note(list, "No body prefab.");
        else if (prefab.GetComponent<EnemyDefinitionApplier>() == null)
            Note(list, "Body prefab has no EnemyDefinitionApplier, so none of this applies in game.",
                 "Add Applier", () => EnemySetupFixes.AddApplier(prefab, arch));

        if (layers != null)
        {
            var seen = new HashSet<EnemyModifierSO>();
            foreach (var m in layers)
            {
                if (m == null) { Note(list, "A modifier slot is empty."); continue; }
                if (!seen.Add(m)) Note(list, $"'{m.name}' is listed twice.");
                foreach (var e in m.stats)
                    if (e.stat == null) { Note(list, $"{m.StatSourceLabel}: a stat entry has no stat."); break; }
            }
        }

        if (prefab == null) return list;

        bool hasDeath = arch.deathEffects.Exists(e => e != null);
        if (!hasDeath && layers != null)
            foreach (var m in layers)
                if (m != null && m.deathEffects.Exists(e => e != null)) { hasDeath = true; break; }
        if (hasDeath && prefab.GetComponentInChildren<Combat.Core.WeaponHitResolver>(true) == null)
            Note(list, "Has death effects but the body prefab has no WeaponHitResolver, so they won't fire.");

        var noShader = new HashSet<string>();
        var noMask = new HashSet<string>();
        var noEmission = new HashSet<string>();
        foreach (var r in EnemyLook.FindRenderers(prefab))
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) continue;
                if (mat.shader == null || mat.shader.name != ZoneShaderSO.EnemyShaderName) noShader.Add(mat.name);
                else if (look.Zones.Count > 0 && mat.GetTexture("_ZoneMask") == null) noMask.Add(mat.name);
                if (look.Emission != Color.black && !mat.IsKeywordEnabled("_EMISSION")) noEmission.Add(mat.name);
            }
        if (noShader.Count > 0)
            Note(list, "Not set up for zone looks (needs FPS0/EnemyLit): " + string.Join(", ", noShader),
                 "Set Up Materials", () => EnemySetupFixes.SetUpMaterials(prefab));
        if (noMask.Count > 0)
            Note(list, "No zone mask assigned, so zone looks don't show on: " + string.Join(", ", noMask),
                 "Open Zone Editor", () => EnemySetupFixes.OpenZoneEditor(prefab));
        if (noEmission.Count > 0) Note(list, "Emission is off on: " + string.Join(", ", noEmission));
        return list;
    }
}

// One-click setup for a new enemy body: applier on the prefab, materials on FPS0/EnemyLit.
public static class EnemySetupFixes
{
    private const string UrpLit = "Universal Render Pipeline/Lit";

    public static void AddApplier(GameObject prefab, EnemyArchetypeSO arch)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path)) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<EnemyDefinitionApplier>() == null)
            {
                var applier = root.AddComponent<EnemyDefinitionApplier>();
                var so = new SerializedObject(applier);
                so.FindProperty("archetype").objectReferenceValue = arch;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        Debug.Log($"[Enemies] Added EnemyDefinitionApplier to '{prefab.name}'.");
    }

    // Model-embedded materials are extracted next to the model and remapped; URP Lit materials switch to FPS0/EnemyLit.
    public static void SetUpMaterials(GameObject prefab)
    {
        var enemyLit = Shader.Find(ZoneShaderSO.EnemyShaderName);
        if (enemyLit == null) { Debug.LogError($"[Enemies] Shader '{ZoneShaderSO.EnemyShaderName}' not found."); return; }

        var mats = new List<Material>();
        foreach (var r in EnemyLook.FindRenderers(prefab))
            foreach (var m in r.sharedMaterials)
                if (m != null && !mats.Contains(m)) mats.Add(m);

        var skipped = new List<string>();
        int converted = 0;
        foreach (var mat in mats)
        {
            var target = AssetDatabase.IsSubAsset(mat) ? Extract(mat) : mat;
            if (target == null) { skipped.Add(mat.name + " (could not extract)"); continue; }
            if (target.shader != null && target.shader.name == ZoneShaderSO.EnemyShaderName) continue;
            if (target.shader == null || target.shader.name != UrpLit)
            {
                skipped.Add($"{target.name} ({(target.shader != null ? target.shader.name : "no shader")})");
                continue;
            }
            Undo.RecordObject(target, "Set Up Enemy Materials");
            target.shader = enemyLit;
            EditorUtility.SetDirty(target);
            converted++;
        }
        AssetDatabase.SaveAssets();

        Debug.Log($"[Enemies] '{prefab.name}': {converted} material(s) switched to {ZoneShaderSO.EnemyShaderName}.");
        if (skipped.Count > 0)
            Debug.LogWarning("[Enemies] Not converted (only URP Lit converts automatically): " + string.Join(", ", skipped));
    }

    // Pulls a material out of its model into <model folder>/Materials and points the model at it.
    private static Material Extract(Material embedded)
    {
        string modelPath = AssetDatabase.GetAssetPath(embedded);
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) return null;

        string name = embedded.name;
        string folder = System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/') + "/Materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), "Materials");

        string dest = folder + "/" + name + ".mat";
        var external = AssetDatabase.LoadAssetAtPath<Material>(dest) ?? FindExternal(name, System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/'));
        if (external == null)
        {
            string error = AssetDatabase.ExtractAsset(embedded, dest);
            if (!string.IsNullOrEmpty(error)) { Debug.LogError($"[Enemies] Extract '{name}' failed: {error}"); return null; }
            AssetDatabase.WriteImportSettingsIfDirty(modelPath);
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            external = AssetDatabase.LoadAssetAtPath<Material>(dest);
        }
        if (external == null) return null;

        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), external);
        importer.SaveAndReimport();
        return external;
    }

    // An already-extracted material with the same name anywhere under the model's folder.
    private static Material FindExternal(string name, string modelFolder)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Material " + name, new[] { modelFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".mat")) continue;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null && m.name == name) return m;
        }
        return null;
    }

    public static void OpenZoneEditor(GameObject prefab)
    {
        AssetDatabase.OpenAsset(prefab);
        EditorWindow.GetWindow<ZoneEditor>("Zones");
    }
}

// The shared section under each enemy inspector.
public static class EnemyDefinitionGUI
{
    private static GUIStyle wrap;

    public static void Section(EnemyPreview preview, string displayName, EnemyArchetypeSO arch,
                               IReadOnlyList<EnemyModifierSO> layers, GameObject prefab)
    {
        if (wrap == null) wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
        var look = EnemyLook.Resolve(layers);

        EditorGUILayout.Space(10f);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            preview.ShowPlain = GUILayout.Toggle(preview.ShowPlain, "Show plain", EditorStyles.miniButton, GUILayout.Width(80f));
        }
        EditorGUILayout.LabelField(displayName, EditorStyles.largeLabel);
        preview.Draw(prefab, look, 300f);
        EditorGUILayout.LabelField("Drag to rotate, scroll to zoom.", EditorStyles.centeredGreyMiniLabel);

        foreach (var c in EnemyDefinitionReport.Checks(arch, layers, prefab, look))
        {
            EditorGUILayout.HelpBox(c.Text, MessageType.Warning);
            if (c.Fix == null) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(c.FixLabel, GUILayout.Width(140f)))
                {
                    var fix = c.Fix;
                    EditorApplication.delayCall += () => fix();
                }
            }
        }

        if (arch == null) return;

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Final Stats", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Stat", EditorStyles.miniBoldLabel, GUILayout.MinWidth(100f));
            EditorGUILayout.LabelField("Base", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
            EditorGUILayout.LabelField("Changes", EditorStyles.miniBoldLabel, GUILayout.MinWidth(80f));
            EditorGUILayout.LabelField("Final", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
        }
        foreach (var r in EnemyDefinitionReport.BuildStats(arch, layers, prefab))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(EnemyDefinitionReport.StatName(r.Stat), r.Stat.id), GUILayout.MinWidth(100f));
                EditorGUILayout.LabelField(new GUIContent(EnemyDefinitionReport.Value(r.Stat, r.Base), "From " + r.BaseSource), GUILayout.Width(70f));
                EditorGUILayout.LabelField(r.Changes.Length > 0 ? r.Changes.ToString() : "-", wrap, GUILayout.MinWidth(80f));
                EditorGUILayout.LabelField(EnemyDefinitionReport.Value(r.Stat, r.Final), EditorStyles.boldLabel, GUILayout.Width(60f));
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
        Summary(arch, layers);
    }

    private static void Summary(EnemyArchetypeSO arch, IReadOnlyList<EnemyModifierSO> layers)
    {
        var rank = arch.rank;
        var loot = arch.lootTable;
        var meleeType = arch.meleeDamageType;
        bool guaranteed = false;
        int extra = 0;
        var melee = new List<string>();
        var death = new List<string>();
        var affixes = new List<string>();
        Names(arch.meleeHitEffects, melee);
        Names(arch.deathEffects, death);
        Names(arch.affixes, affixes);

        if (layers != null)
            foreach (var m in layers)
            {
                if (m == null) continue;
                if ((int)m.raiseRankTo > (int)rank) rank = (EnemyRank)m.raiseRankTo;
                if (m.lootTable != null) loot = m.lootTable;
                if (m.meleeDamageType != null) meleeType = m.meleeDamageType;
                guaranteed |= m.guaranteedDrop;
                extra += m.extraDrops;
                Names(m.meleeHitEffects, melee);
                Names(m.deathEffects, death);
                Names(m.affixes, affixes);
            }

        Line("Rank", rank.ToString());
        Line("Loot", (loot != null ? loot.name : "prefab table")
                     + (guaranteed ? ", guaranteed drop" : "") + (extra > 0 ? $", +{extra} rolls" : ""));
        Line("Melee type", meleeType != null ? meleeType.displayName : "prefab default");
        Line("On-hit effects", melee.Count > 0 ? string.Join(", ", melee) : "-");
        Line("Death effects", death.Count > 0 ? string.Join(", ", death) : "-");
        Line("Affixes", affixes.Count > 0 ? string.Join(", ", affixes) : "-");
    }

    private static void Names<T>(List<T> src, List<string> dst) where T : Object
    {
        foreach (var o in src) if (o != null) dst.Add(o.name);
    }

    private static void Line(string label, string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUILayout.LabelField(value, wrap);
        }
    }

    // A modifier's look preset drawn inline with its own editor, so colours can be tuned beside the preview.
    public static void LookPreset(EnemyModifierSO m, Dictionary<Object, Editor> cache)
    {
        if (m == null || m.look == null) return;
        string key = "FPS0.EnemyLook." + m.look.GetInstanceID();
        bool open = SessionState.GetBool(key, true);
        bool now = EditorGUILayout.Foldout(open, "Look: " + m.look.name + "  (shared preset)", true);
        if (now != open) SessionState.SetBool(key, now);
        if (!now) return;

        cache.TryGetValue(m.look, out var ed);
        Editor.CreateCachedEditor(m.look, null, ref ed);
        cache[m.look] = ed;
        using (new EditorGUI.IndentLevelScope())
            ed.OnInspectorGUI();
    }

    public static void DestroyEditors(Dictionary<Object, Editor> cache)
    {
        foreach (var ed in cache.Values)
            if (ed != null) Object.DestroyImmediate(ed);
        cache.Clear();
    }

    // Opens the asset in the Enemy Workshop. Hidden when already drawn inside it.
    public static void WorkshopButton(Object target)
    {
        if (EnemyWorkshop.Drawing) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open in Workshop", EditorStyles.miniButton, GUILayout.Width(120f))) EnemyWorkshop.Open(target);
        }
    }

    // Plain property list for an object, without its custom editor.
    public static void DrawPlain(SerializedObject so)
    {
        so.Update();
        var p = so.GetIterator();
        bool enter = true;
        while (p.NextVisible(enter))
        {
            enter = false;
            if (p.propertyPath == "m_Script") continue;
            EditorGUILayout.PropertyField(p, true);
        }
        so.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(EnemyVariantSO))]
public class EnemyVariantEditor : Editor
{
    private readonly EnemyPreview preview = new EnemyPreview();
    private readonly Dictionary<EnemyModifierSO, SerializedObject> layerObjects = new Dictionary<EnemyModifierSO, SerializedObject>();
    private readonly List<EnemyModifierSO> layers = new List<EnemyModifierSO>();
    private readonly Dictionary<Object, Editor> lookEditors = new Dictionary<Object, Editor>();

    private void OnDisable()
    {
        preview.Cleanup();
        EnemyDefinitionGUI.DestroyEditors(lookEditors);
    }

    public override void OnInspectorGUI()
    {
        EnemyDefinitionGUI.WorkshopButton(target);
        DrawDefaultInspector();
        var v = (EnemyVariantSO)target;

        layers.Clear();
        foreach (var m in v.modifiers) layers.Add(m);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
        foreach (var m in layers)
        {
            if (m == null) continue;
            string key = "FPS0.EnemyLayer." + m.GetInstanceID();
            bool open = SessionState.GetBool(key, false);
            bool now = EditorGUILayout.Foldout(open, m.StatSourceLabel + "  (shared: edits apply to every variant using it)", true);
            if (now != open) SessionState.SetBool(key, now);
            if (!now) continue;

            if (!layerObjects.TryGetValue(m, out var so) || so.targetObject == null)
                layerObjects[m] = so = new SerializedObject(m);
            using (new EditorGUI.IndentLevelScope())
            {
                EnemyDefinitionGUI.DrawPlain(so);
                EnemyDefinitionGUI.LookPreset(m, lookEditors);
            }
        }

        EnemyDefinitionGUI.Section(preview, v.DisplayName, v.archetype, layers, v.Prefab);
    }
}

[CustomEditor(typeof(EnemyModifierSO))]
public class EnemyModifierEditor : Editor
{
    private const string ArchetypeKey = "FPS0.EnemyModifierPreviewArchetype";
    private readonly EnemyPreview preview = new EnemyPreview();
    private readonly List<EnemyModifierSO> layers = new List<EnemyModifierSO>(1);
    private readonly Dictionary<Object, Editor> lookEditors = new Dictionary<Object, Editor>();

    private void OnDisable()
    {
        preview.Cleanup();
        EnemyDefinitionGUI.DestroyEditors(lookEditors);
    }

    public override void OnInspectorGUI()
    {
        EnemyDefinitionGUI.WorkshopButton(target);
        DrawDefaultInspector();
        var m = (EnemyModifierSO)target;
        EnemyDefinitionGUI.LookPreset(m, lookEditors);

        EditorGUILayout.Space(8f);
        var arch = LoadArchetype();
        var picked = (EnemyArchetypeSO)EditorGUILayout.ObjectField("Preview On", arch, typeof(EnemyArchetypeSO), false);
        if (picked != arch)
        {
            arch = picked;
            EditorPrefs.SetString(ArchetypeKey, arch != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(arch)) : "");
        }

        layers.Clear();
        layers.Add(m);
        string name = (string.IsNullOrEmpty(m.namePrefix) ? "" : m.namePrefix + " ") + (arch != null ? arch.StatSourceLabel : "");
        EnemyDefinitionGUI.Section(preview, name, arch, layers, arch != null ? arch.bodyPrefab : null);
    }

    // Last picked archetype, else the first one in the project.
    private static EnemyArchetypeSO LoadArchetype()
    {
        string guid = EditorPrefs.GetString(ArchetypeKey, "");
        if (!string.IsNullOrEmpty(guid))
        {
            var a = AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) return a;
        }
        var found = AssetDatabase.FindAssets("t:EnemyArchetypeSO");
        return found.Length > 0 ? AssetDatabase.LoadAssetAtPath<EnemyArchetypeSO>(AssetDatabase.GUIDToAssetPath(found[0])) : null;
    }
}

[CustomEditor(typeof(EnemyArchetypeSO))]
public class EnemyArchetypeEditor : Editor
{
    private readonly EnemyPreview preview = new EnemyPreview();

    private void OnDisable() => preview.Cleanup();

    public override void OnInspectorGUI()
    {
        EnemyDefinitionGUI.WorkshopButton(target);
        DrawDefaultInspector();
        var a = (EnemyArchetypeSO)target;
        EnemyDefinitionGUI.Section(preview, a.StatSourceLabel, a, null, a.bodyPrefab);
    }
}
