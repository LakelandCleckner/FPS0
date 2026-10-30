using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Combat.Effects;
using Combat.Perks;

// Creates and updates Effect Perk assets from one line of text per perk.
//   Name | EffectAsset, EffectAsset | description
public class EffectPerkBatchWindow : EditorWindow
{
    private sealed class Parsed
    {
        public string Name;
        public string Description;
        public List<HitEffectSO> Effects = new List<HitEffectSO>();
    }

    [SerializeField] private string folder = "Assets/GameData/Perks/Weapon/Effect";
    [SerializeField] private string text = "";

    private Vector2 scroll;
    private readonly List<Parsed> parsed = new List<Parsed>();
    private readonly List<string> errors = new List<string>();
    private Dictionary<string, HitEffectSO> effects;
    private HashSet<string> ambiguous;

    [MenuItem("Tools/Effect Perk Batch")]
    private static void Open() => GetWindow<EffectPerkBatchWindow>("Effect Perks");

    private void OnEnable() => Reparse();

    private void OnGUI()
    {
        folder = EditorGUILayout.TextField("Folder", folder);
        EditorGUILayout.HelpBox(
            "Name | EffectAsset, EffectAsset | description\n" +
            "Effects are matched by asset name (case and spaces ignored). Lines starting with # are ignored.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load Folder")) Load();
            if (GUILayout.Button("Refresh Effects")) { effects = null; Reparse(); }
        }

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        EditorGUI.BeginChangeCheck();
        text = EditorGUILayout.TextArea(text, GUILayout.ExpandHeight(true));
        if (EditorGUI.EndChangeCheck()) Reparse();
        EditorGUILayout.EndScrollView();

        if (errors.Count > 0)
            EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Error);
        else if (parsed.Count > 0)
            EditorGUILayout.HelpBox(Preview(), MessageType.Info);

        using (new EditorGUI.DisabledScope(errors.Count > 0 || parsed.Count == 0))
        {
            if (GUILayout.Button($"Apply {parsed.Count} Perk(s)", GUILayout.Height(28))) Apply();
        }
    }

    private string Preview()
    {
        var sb = new StringBuilder();
        foreach (var p in parsed)
        {
            sb.Append(p.Name).Append(": ");
            for (int i = 0; i < p.Effects.Count; i++)
                sb.Append(i > 0 ? ", " : "").Append(p.Effects[i].name);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ parse

    private void Reparse()
    {
        parsed.Clear();
        errors.Clear();
        EnsureLookup();

        var lines = (text ?? "").Replace("\r", "").Split('\n');
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            var parts = line.Split('|');
            if (parts.Length < 2) { errors.Add($"Line {n + 1}: expected 'Name | effects'"); continue; }

            var p = new Parsed
            {
                Name = parts[0].Trim(),
                Description = parts.Length > 2 ? string.Join("|", parts, 2, parts.Length - 2).Trim() : ""
            };

            if (p.Name.Length == 0) { errors.Add($"Line {n + 1}: missing name"); continue; }
            if (!names.Add(p.Name)) { errors.Add($"Line {n + 1}: duplicate name '{p.Name}'"); continue; }

            bool ok = true;
            foreach (var raw in parts[1].Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                string key = Key(name);
                if (ambiguous.Contains(key)) { errors.Add($"Line {n + 1}: more than one effect asset is named '{name}'"); ok = false; }
                else if (!effects.TryGetValue(key, out var fx)) { errors.Add($"Line {n + 1}: unknown effect '{name}'"); ok = false; }
                else p.Effects.Add(fx);
            }

            if (ok && p.Effects.Count == 0) { errors.Add($"Line {n + 1}: no effects"); ok = false; }
            if (ok) parsed.Add(p);
        }
    }

    // ------------------------------------------------------------------ load / apply

    private void Load()
    {
        var sb = new StringBuilder();
        if (AssetDatabase.IsValidFolder(folder))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:EffectPerkSO", new[] { folder }))
            {
                var perk = AssetDatabase.LoadAssetAtPath<EffectPerkSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (perk == null) continue;

                sb.Append(string.IsNullOrEmpty(perk.displayName) ? perk.name : perk.displayName).Append(" | ");
                int k = 0;
                foreach (var fx in perk.effects)
                    if (fx != null) sb.Append(k++ > 0 ? ", " : "").Append(fx.name);
                if (!string.IsNullOrWhiteSpace(perk.description))
                    sb.Append(" | ").Append(perk.description.Replace("\n", " "));
                sb.Append('\n');
            }
        }
        text = sb.ToString();
        GUI.FocusControl(null);
        Reparse();
    }

    private void Apply()
    {
        EnsureFolder(folder);
        int created = 0, updated = 0;

        foreach (var p in parsed)
        {
            string path = $"{folder}/{FileName(p.Name)}.asset";
            var perk = AssetDatabase.LoadAssetAtPath<EffectPerkSO>(path);
            bool isNew = perk == null;
            if (isNew) perk = CreateInstance<EffectPerkSO>();

            perk.displayName = p.Name;
            perk.id = Id(p.Name);
            perk.description = p.Description;
            perk.effects = new List<HitEffectSO>(p.Effects);

            if (isNew) { AssetDatabase.CreateAsset(perk, path); created++; }
            else { EditorUtility.SetDirty(perk); updated++; }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[EffectPerkBatch] {folder}: {created} created, {updated} updated.");
    }

    // ------------------------------------------------------------------ helpers

    private void EnsureLookup()
    {
        if (effects != null) return;

        effects = new Dictionary<string, HitEffectSO>();
        ambiguous = new HashSet<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:HitEffectSO"))
        {
            var fx = AssetDatabase.LoadAssetAtPath<HitEffectSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (fx == null) continue;

            string key = Key(fx.name);
            if (effects.ContainsKey(key) && effects[key] != fx) ambiguous.Add(key);
            else effects[key] = fx;
        }
    }

    private static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static string FileName(string name)
    {
        var s = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        return s;
    }

    private static string Id(string name) => FileName(name).ToLowerInvariant().Replace(' ', '_');

    private static void EnsureFolder(string path)
    {
        string[] parts = path.TrimEnd('/').Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
