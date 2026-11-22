using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Enemies;
using Combat.Status;
using Combat.Weapons;

// Picks the enemies (archetype, rank, layers) for both gallery lineups, and the element, weapon and statuses for the effects lineup; fires its triggers.
public class EnemyEffectsGalleryWindow : EditorWindow
{
    private List<DamageTypeSO> elements = new List<DamageTypeSO>();
    private List<StatusSO> statuses = new List<StatusSO>();
    private List<WeaponSO> weapons = new List<WeaponSO>();
    private List<ExplosionOnKillSO> explosions = new List<ExplosionOnKillSO>();
    private List<EnemyArchetypeSO> archetypes = new List<EnemyArchetypeSO>();
    private List<EnemyModifierSO> layerOptions = new List<EnemyModifierSO>();
    private WeaponStatKeys statKeys;

    private int element;
    private int weapon;
    private int status;
    private int explosion;
    private string summary;
    private Vector2 scroll;

    [MenuItem("FPS0/Enemies/Gallery Window", priority = 2)]
    private static void Open() => GetWindow<EnemyEffectsGalleryWindow>("Enemy Gallery");

    private void OnEnable() => Refresh();
    private void OnProjectChange() { Refresh(); Repaint(); }
    private void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

    private void Refresh()
    {
        elements = Find<DamageTypeSO>();
        weapons = Find<WeaponSO>();
        explosions = Find<ExplosionOnKillSO>();
        statKeys = Find<WeaponStatKeys>().FirstOrDefault();
        archetypes = Find<EnemyArchetypeSO>();
        layerOptions = Find<EnemyModifierSO>().Where(m => m.kind != EnemyModifierKind.Tier).ToList();
        EnemyTierLayers.Invalidate();
        summary = null;

        // Abstract type: a typed search misses some statuses, so scan every ScriptableObject.
        statuses = new List<StatusSO>();
        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".asset")) continue;
            if (AssetDatabase.GetMainAssetTypeAtPath(path) is System.Type t && typeof(StatusSO).IsAssignableFrom(t))
                statuses.Add(AssetDatabase.LoadAssetAtPath<StatusSO>(path));
        }
        statuses.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    // The entry after the last element is General: statuses with no element, shown with any weapon.
    private DamageTypeSO Element => element >= 0 && element < elements.Count ? elements[element] : null;
    private bool General => element == elements.Count;
    private List<StatusSO> ElementStatuses => statuses.Where(s => s.damageType == Element).ToList();
    private List<WeaponSO> ElementWeapons => General ? weapons : weapons.Where(w => w.baseDamageType == Element).ToList();

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("The effects lineup runs in Play mode in the gallery scene.", MessageType.Info);
            if (GUILayout.Button("Set Up Gallery Scene For Effects")) EnemyGallerySceneBuilder.SetupEffects();
            if (GUILayout.Button("Refresh Assets")) Refresh();
            EditorGUILayout.Space();
        }

        var gallery = FindFirstObjectByType<EnemyGallery>();
        Enemies(gallery);
        Picks();
        var g = Application.isPlaying ? FindFirstObjectByType<EnemyEffectsGallery>() : null;
        if (Application.isPlaying)
        {
            if (GUILayout.Button("Build Lineup", GUILayout.Height(26))) Build(ref g, gallery);
            if (g != null) Controls(g);
        }

        EditorGUILayout.EndScrollView();
    }

    // Archetype, rank and layers on the scene's gallery; the edit-mode lineup follows them live.
    private void Enemies(EnemyGallery gallery)
    {
        EditorGUILayout.LabelField("Enemies", EditorStyles.boldLabel);
        if (gallery == null) { EditorGUILayout.HelpBox("No EnemyGallery in the scene (FPS0/Enemies/Gallery Scene).", MessageType.Warning); return; }
        // Assets deleted since the last refresh.
        if (archetypes.RemoveAll(x => x == null) + layerOptions.RemoveAll(x => x == null) > 0) summary = null;

        var so = new SerializedObject(gallery);
        var arch = so.FindProperty("archetype");
        int a = Mathf.Max(0, archetypes.IndexOf(arch.objectReferenceValue as EnemyArchetypeSO) + 1);
        int na = EditorGUILayout.Popup("Archetype", a, new[] { "All" }.Concat(archetypes.Select(x => x.StatSourceLabel)).ToArray());
        if (na != a) arch.objectReferenceValue = na == 0 ? null : archetypes[na - 1];
        EditorGUILayout.PropertyField(so.FindProperty("rank"));

        var picked = so.FindProperty("layers");
        var current = new List<EnemyModifierSO>();
        for (int i = 0; i < picked.arraySize; i++)
            if (picked.GetArrayElementAtIndex(i).objectReferenceValue is EnemyModifierSO m) current.Add(m);
        EditorGUILayout.LabelField("Layers", EditorStyles.miniBoldLabel);
        int perRow = Mathf.Max(1, (int)((position.width - 20f) / 150f));
        bool changed = false;
        for (int i = 0; i < layerOptions.Count; i += perRow)
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int j = i; j < Mathf.Min(i + perRow, layerOptions.Count); j++)
                {
                    var m = layerOptions[j];
                    bool on = current.Contains(m);
                    if (GUILayout.Toggle(on, m.name, GUILayout.Width(145f)) == on) continue;
                    if (on) current.Remove(m); else current.Add(m);
                    changed = true;
                }
                GUILayout.FlexibleSpace();
            }
        if (changed)
        {
            picked.ClearArray();
            for (int i = 0; i < current.Count; i++)
            {
                picked.InsertArrayElementAtIndex(i);
                picked.GetArrayElementAtIndex(i).objectReferenceValue = current[i];
            }
        }
        EditorGUILayout.PropertyField(so.FindProperty("layerMode"), new GUIContent("Layer Mode", "Combine: a row per mix of one element and one shield, other traits stacked. Separate: a row per layer. All Together: every layer on every enemy."));
        EditorGUILayout.PropertyField(so.FindProperty("plainRow"), new GUIContent("Plain Row", "Also a row with no layers when layers are picked."));
        EditorGUILayout.PropertyField(so.FindProperty("handMade"), new GUIContent("Hand-made Variants"));
        if (so.ApplyModifiedProperties() || summary == null)
        {
            var rows = gallery.Rows();
            summary = rows.Sum(r => r.Count) + " enemies in " + rows.Count + " rows";
        }
        EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);
        EditorGUILayout.Space();
    }

    private void Picks()
    {
        EditorGUILayout.LabelField("Effects", EditorStyles.boldLabel);
        int e = EditorGUILayout.Popup("Element", element, elements.Select(x => x.displayName).Append("General").ToArray());
        if (e != element) { element = e; weapon = 0; status = 0; explosion = 0; }

        var ws = ElementWeapons;
        weapon = EditorGUILayout.Popup(new GUIContent("Weapon", "Its round, tracer, damage and hit pipeline are used."), Mathf.Min(weapon, Mathf.Max(0, ws.Count - 1)), ws.Select(w => w.name).ToArray());

        var ss = ElementStatuses;
        status = EditorGUILayout.Popup("Status", status, new[] { "All (" + ss.Count + ")" }.Concat(ss.Select(EnemyEffectsGallery.StatusName)).ToArray());

        var ex = explosions.Where(x => x.damageType == Element).ToList();
        explosion = EditorGUILayout.Popup(new GUIContent("Kill Explosion", "On-kill explosion added to Kill."), Mathf.Min(explosion, ex.Count), new[] { "None" }.Concat(ex.Select(x => x.name)).ToArray());
    }

    private void Build(ref EnemyEffectsGallery g, EnemyGallery gallery)
    {
        if (gallery == null) { Debug.LogWarning("[Gallery] No EnemyGallery in the scene."); return; }
        var ws = ElementWeapons;
        if (ws.Count == 0) { Debug.LogWarning("[Gallery] No weapon of this element."); return; }
        if (statKeys == null) { Debug.LogWarning("[Gallery] No WeaponStatKeys asset."); return; }

        if (g == null) g = gallery.gameObject.AddComponent<EnemyEffectsGallery>();

        var ss = ElementStatuses;
        var pickStatuses = status == 0 ? ss : new List<StatusSO> { ss[status - 1] };
        var galleryRows = gallery.Rows();

        var rows = new List<EnemyEffectsGallery.Row>();
        // Statuses with stack levels get a row per enemy; single-level ones keep a gallery row side by side.
        foreach (var st in pickStatuses)
        {
            bool levels = st.PreviewLevels().Count > 1;
            foreach (var gr in galleryRows)
            {
                if (levels)
                    foreach (var body in gr)
                    {
                        var one = new EnemyEffectsGallery.Row { Status = st, Body = body.Label };
                        one.Bodies.Add(body);
                        rows.Add(one);
                    }
                else
                {
                    var row = new EnemyEffectsGallery.Row { Status = st, Body = gr[0].Label };
                    row.Bodies.AddRange(gr);
                    rows.Add(row);
                }
            }
        }

        var ex = explosions.Where(x => x.damageType == Element).ToList();
        g.KillExplosion = explosion > 0 && explosion <= ex.Count ? ex[explosion - 1] : null;
        g.Build(ws[Mathf.Min(weapon, ws.Count - 1)], statKeys, rows);
        if (g.Rows.Count > 0) g.Frame(g.Rows[0]);
    }

    private void Controls(EnemyEffectsGallery g)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("All", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Fire")) g.FireAll();
            if (GUILayout.Button("Blast")) g.BlastAll();
            if (GUILayout.Button("Arc")) g.ArcAll();
            if (GUILayout.Button("Convert")) g.ConvertAll();
            if (GUILayout.Button("Kill")) g.KillAll();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Break Shields")) g.BreakShields();
            if (GUILayout.Button("Restore Shields")) g.RestoreShields();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset Statuses")) g.ResetStatuses();
            if (GUILayout.Button("Respawn")) g.RespawnAll();
            if (GUILayout.Button("Clear")) g.Clear();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Loop", EditorStyles.boldLabel);
        g.HoldStatuses = EditorGUILayout.Toggle(new GUIContent("Hold Statuses", "Keep each enemy at its stack count."), g.HoldStatuses);
        g.Loop = EditorGUILayout.Toggle("Loop", g.Loop);
        using (new EditorGUI.DisabledScope(!g.Loop))
        {
            g.LoopInterval = EditorGUILayout.Slider("Interval (s)", g.LoopInterval, 0.1f, 5f);
            g.LoopFire = EditorGUILayout.Toggle("Fire", g.LoopFire);
            g.LoopBlast = EditorGUILayout.Toggle("Blast", g.LoopBlast);
        }
        g.BlastRadius = EditorGUILayout.Slider("Blast Radius (m)", g.BlastRadius, 0.5f, 10f);
        g.ShowLabels = EditorGUILayout.Toggle("Labels", g.ShowLabels);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Time", EditorStyles.boldLabel);
        Time.timeScale = EditorGUILayout.Slider("Time Scale", Time.timeScale, 0.05f, 1f);
        EditorApplication.isPaused = EditorGUILayout.Toggle("Pause", EditorApplication.isPaused);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rows", EditorStyles.boldLabel);
        foreach (var r in g.Rows)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(r.Label, GUILayout.MinWidth(120));
                if (GUILayout.Button("Fire", GUILayout.Width(44))) g.FireRow(r);
                if (GUILayout.Button("Blast", GUILayout.Width(48))) g.BlastRow(r);
                if (GUILayout.Button("Arc", GUILayout.Width(38))) g.Arc(r);
                using (new EditorGUI.DisabledScope(r.Status == null || r.Status.convertsTo == null))
                    if (GUILayout.Button("Convert", GUILayout.Width(62))) g.ConvertRow(r);
                if (GUILayout.Button("Kill", GUILayout.Width(38))) g.KillRow(r);
                if (GUILayout.Button("Frame", GUILayout.Width(50))) g.Frame(r);
            }
    }

    private static List<T> Find<T>() where T : Object
    {
        var list = new List<T>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) list.Add(a);
        }
        list.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        return list;
    }
}
