using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Combat.Core;
using Combat.Effects;
using Combat.Enemies;
using Combat.Status;
using Combat.Weapons;

// Picks the element, weapon, statuses and bodies for the gallery's effects lineup and fires its triggers.
public class EnemyEffectsGalleryWindow : EditorWindow
{
    private class Body
    {
        public string Label;
        public GameObject Prefab;
        public EnemyVariantSO Variant;
        public EnemyArchetypeSO Archetype;
    }

    private List<DamageTypeSO> elements = new List<DamageTypeSO>();
    private List<StatusSO> statuses = new List<StatusSO>();
    private List<WeaponSO> weapons = new List<WeaponSO>();
    private List<ExplosionOnKillSO> explosions = new List<ExplosionOnKillSO>();
    private List<EnemyModifierSO> shieldLayers = new List<EnemyModifierSO>();
    private List<Body> bodies = new List<Body>();
    private WeaponStatKeys statKeys;

    private int element;
    private int weapon;
    private int status;
    private int body;
    private int explosion;
    private int shieldLayer;
    private Vector2 scroll;

    [MenuItem("FPS0/Enemies/Gallery Window", priority = 2)]
    private static void Open() => GetWindow<EnemyEffectsGalleryWindow>("Enemy Gallery");

    private void OnEnable() => Refresh();
    private void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

    private void Refresh()
    {
        elements = Find<DamageTypeSO>();
        weapons = Find<WeaponSO>();
        explosions = Find<ExplosionOnKillSO>();
        statKeys = Find<WeaponStatKeys>().FirstOrDefault();
        shieldLayers = Find<EnemyModifierSO>().Where(m => m.shield != null).ToList();

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

        bodies = new List<Body>();
        var variants = Find<EnemyVariantSO>();
        foreach (var arch in Find<EnemyArchetypeSO>())
        {
            bodies.Add(new Body { Label = arch.StatSourceLabel, Prefab = arch.bodyPrefab, Archetype = arch });
            foreach (var v in variants.Where(v => v.archetype == arch))
                bodies.Add(new Body { Label = arch.StatSourceLabel + "/" + v.DisplayName, Prefab = v.Prefab, Variant = v, Archetype = arch });
        }
    }

    private DamageTypeSO Element => element >= 0 && element < elements.Count ? elements[element] : null;
    private List<StatusSO> ElementStatuses => statuses.Where(s => s.damageType == Element).ToList();
    private List<WeaponSO> ElementWeapons => weapons.Where(w => w.baseDamageType == Element).ToList();

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

        Picks();
        var g = Application.isPlaying ? FindFirstObjectByType<EnemyEffectsGallery>() : null;
        if (Application.isPlaying)
        {
            if (GUILayout.Button("Build Lineup", GUILayout.Height(26))) Build(ref g);
            if (g != null) Controls(g);
        }

        EditorGUILayout.EndScrollView();
    }

    private void Picks()
    {
        EditorGUILayout.LabelField("Lineup", EditorStyles.boldLabel);
        int e = EditorGUILayout.Popup("Element", element, elements.Select(x => x.displayName).ToArray());
        if (e != element) { element = e; weapon = 0; status = 0; explosion = 0; }

        var ws = ElementWeapons;
        weapon = EditorGUILayout.Popup(new GUIContent("Weapon", "Its round, tracer, damage and hit pipeline are used."), Mathf.Min(weapon, Mathf.Max(0, ws.Count - 1)), ws.Select(w => w.name).ToArray());

        var ss = ElementStatuses;
        status = EditorGUILayout.Popup("Status", status, new[] { "All (" + ss.Count + ")" }.Concat(ss.Select(EnemyEffectsGallery.StatusName)).ToArray());

        body = EditorGUILayout.Popup("Body", body, new[] { "One of each archetype" }.Concat(bodies.Select(b => b.Label)).ToArray());

        var ex = explosions.Where(x => x.damageType == Element).ToList();
        explosion = EditorGUILayout.Popup(new GUIContent("Kill Explosion", "On-kill explosion added to Kill."), Mathf.Min(explosion, ex.Count), new[] { "None" }.Concat(ex.Select(x => x.name)).ToArray());
        shieldLayer = EditorGUILayout.Popup(new GUIContent("Shield", "Shield layer added to every enemy in the lineup."), Mathf.Min(shieldLayer, shieldLayers.Count), new[] { "None" }.Concat(shieldLayers.Select(m => m.name)).ToArray());
    }

    private void Build(ref EnemyEffectsGallery g)
    {
        var ws = ElementWeapons;
        if (ws.Count == 0) { Debug.LogWarning("[Gallery] No weapon of this element."); return; }
        if (statKeys == null) { Debug.LogWarning("[Gallery] No WeaponStatKeys asset."); return; }

        if (g == null)
        {
            var host = FindFirstObjectByType<EnemyGallery>();
            var go = host != null ? host.gameObject : new GameObject("Effects Gallery");
            g = go.AddComponent<EnemyEffectsGallery>();
        }

        var ss = ElementStatuses;
        var pickStatuses = status == 0 ? ss : new List<StatusSO> { ss[status - 1] };
        var pickBodies = body == 0 ? bodies.Where(b => b.Variant == null).ToList() : new List<Body> { bodies[body - 1] };

        var rows = new List<EnemyEffectsGallery.Row>();
        foreach (var st in pickStatuses)
            foreach (var b in pickBodies)
                rows.Add(new EnemyEffectsGallery.Row { Status = st, Prefab = b.Prefab, Variant = b.Variant, Archetype = b.Archetype, Body = b.Label });

        var ex = explosions.Where(x => x.damageType == Element).ToList();
        g.KillExplosion = explosion > 0 && explosion <= ex.Count ? ex[explosion - 1] : null;
        g.ExtraLayers.Clear();
        if (shieldLayer > 0 && shieldLayer <= shieldLayers.Count) g.ExtraLayers.Add(shieldLayers[shieldLayer - 1]);
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
