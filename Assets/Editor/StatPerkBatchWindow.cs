using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Combat.Loadout;
using Combat.Perks;
using Combat.Stats;
using Combat.Weapons;

// Creates and updates Stat Perk assets from one line of text per perk.
//   Name | stat bucket value [player|weapons] [hands|loadout] [types=A,B] ; ... | description
// Comma values make tiers: "Range | range flat 5,10,15" -> Range I, II, III.
public class StatPerkBatchWindow : EditorWindow
{
    private enum Kind { Weapon, Armor }

    private sealed class Parsed
    {
        public string Name;
        public string Description;
        public List<ModifierEntry> Modifiers = new List<ModifierEntry>();
    }

    private const string Root = "Assets/GameData/Perks";
    private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

    [SerializeField] private Kind kind = Kind.Weapon;
    [SerializeField] private string subfolder = "Stat";
    [SerializeField] private string text = "";

    private Vector2 scroll;
    private readonly List<Parsed> parsed = new List<Parsed>();
    private readonly List<string> errors = new List<string>();
    private Dictionary<string, StatDefinitionSO> stats;
    private Dictionary<string, WeaponTypeSO> types;

    [MenuItem("Tools/Stat Perk Batch")]
    private static void Open() => GetWindow<StatPerkBatchWindow>("Stat Perks");

    private void OnEnable() => Reparse();

    private string Folder
    {
        get
        {
            string f = Root + "/" + kind;
            return string.IsNullOrWhiteSpace(subfolder) ? f : f + "/" + subfolder.Trim();
        }
    }

    private bool DefaultPlayer => kind == Kind.Armor;

    // ------------------------------------------------------------------ UI

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        kind = (Kind)EditorGUILayout.EnumPopup("Perks For", kind);
        subfolder = EditorGUILayout.TextField("Subfolder", subfolder);
        if (EditorGUI.EndChangeCheck()) Reparse();

        EditorGUILayout.LabelField("Folder", Folder, EditorStyles.miniLabel);
        EditorGUILayout.HelpBox(
            "Name | stat bucket value [player|weapons] [hands|loadout] [types=A,B] ; more mods | description\n" +
            "bucket: flat, add, mult.  Default target: " + (DefaultPlayer ? "player" : "weapons") + ", scope loadout.\n" +
            "Comma values make tiers (5,10,15 -> I, II, III). Lines starting with # are ignored.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load Folder")) Load();
            if (GUILayout.Button("Refresh Stats")) { stats = null; types = null; Reparse(); }
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
        int shown = 0;
        foreach (var p in parsed)
        {
            if (shown++ == 12) { sb.Append($"...and {parsed.Count - 12} more"); break; }
            sb.Append(p.Name).Append(": ");
            for (int i = 0; i < p.Modifiers.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Describe(p.Modifiers[i]));
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ parse

    private void Reparse()
    {
        parsed.Clear();
        errors.Clear();
        EnsureLookups();

        var lines = (text ?? "").Replace("\r", "").Split('\n');
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            try
            {
                foreach (var p in ParseLine(line))
                {
                    if (!names.Add(p.Name)) throw new FormatException($"duplicate name '{p.Name}'");
                    parsed.Add(p);
                }
            }
            catch (FormatException e)
            {
                errors.Add($"Line {n + 1}: {e.Message}");
            }
        }
    }

    private IEnumerable<Parsed> ParseLine(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 2) throw new FormatException("expected 'Name | modifiers'");

        string name = parts[0].Trim();
        if (name.Length == 0) throw new FormatException("missing name");
        string desc = parts.Length > 2 ? string.Join("|", parts, 2, parts.Length - 2).Trim() : "";

        // Each modifier with its tier values.
        var mods = new List<(ModifierEntry entry, float[] values)>();
        int tiers = 1;

        foreach (var raw in parts[1].Split(';'))
        {
            string m = raw.Trim();
            if (m.Length == 0) continue;

            var mod = ParseModifier(m, out float[] values);
            if (values.Length > 1)
            {
                if (tiers > 1 && values.Length != tiers)
                    throw new FormatException($"'{m}' has {values.Length} tiers, another modifier has {tiers}");
                tiers = values.Length;
            }
            mods.Add((mod, values));
        }

        if (mods.Count == 0) throw new FormatException("no modifiers");
        if (tiers > Roman.Length) throw new FormatException($"at most {Roman.Length} tiers");

        for (int t = 0; t < tiers; t++)
        {
            var p = new Parsed
            {
                Name = tiers > 1 ? $"{name} {Roman[t]}" : name,
                Description = desc
            };

            foreach (var m in mods)
            {
                var copy = Clone(m.entry);
                copy.value = m.values.Length > 1 ? m.values[t] : m.values[0];
                p.Modifiers.Add(copy);
            }
            yield return p;
        }
    }

    private ModifierEntry ParseModifier(string text, out float[] values)
    {
        var tok = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 3) throw new FormatException($"'{text}': expected 'stat bucket value'");

        if (!stats.TryGetValue(Key(tok[0]), out var stat))
            throw new FormatException($"unknown stat '{tok[0]}'");

        var e = new ModifierEntry
        {
            stat = stat,
            bucket = ParseBucket(tok[1]),
            target = DefaultPlayer ? ModifierTarget.Player : ModifierTarget.Weapons,
            scope = ActivityScope.InLoadout
        };

        var vals = tok[2].Split(',');
        values = new float[vals.Length];
        for (int i = 0; i < vals.Length; i++)
            if (!float.TryParse(vals[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                throw new FormatException($"'{vals[i]}' is not a number");

        for (int i = 3; i < tok.Length; i++)
        {
            string t = tok[i].ToLowerInvariant();
            switch (t)
            {
                case "player": e.target = ModifierTarget.Player; break;
                case "weapons": case "weapon": e.target = ModifierTarget.Weapons; break;
                case "hands": e.scope = ActivityScope.InHands; break;
                case "loadout": e.scope = ActivityScope.InLoadout; break;
                default:
                    if (!t.StartsWith("types=")) throw new FormatException($"unknown option '{tok[i]}'");
                    foreach (var name in tok[i].Substring(6).Split(','))
                    {
                        if (name.Length == 0) continue;
                        if (!types.TryGetValue(Key(name), out var type))
                            throw new FormatException($"unknown weapon type '{name}'");
                        e.weaponTypes.Add(type);
                    }
                    break;
            }
        }
        return e;
    }

    private static ModifierBucket ParseBucket(string s)
    {
        switch (s.ToLowerInvariant())
        {
            case "flat": return ModifierBucket.Flat;
            case "add": case "additive": return ModifierBucket.Additive;
            case "mult": case "multiplicative": return ModifierBucket.Multiplicative;
            default: throw new FormatException($"unknown bucket '{s}' (flat, add, mult)");
        }
    }

    // ------------------------------------------------------------------ load / apply

    // Turns the folder's Stat Perks back into lines (tiers come back as separate lines).
    private void Load()
    {
        var sb = new StringBuilder();
        if (AssetDatabase.IsValidFolder(Folder))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:StatPerkSO", new[] { Folder }))
            {
                var perk = AssetDatabase.LoadAssetAtPath<StatPerkSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (perk == null) continue;

                sb.Append(string.IsNullOrEmpty(perk.displayName) ? perk.name : perk.displayName).Append(" | ");
                for (int i = 0; i < perk.modifiers.Count; i++)
                {
                    if (i > 0) sb.Append(" ; ");
                    sb.Append(Describe(perk.modifiers[i]));
                }
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
        EnsureFolder(Folder);
        int created = 0, updated = 0;

        foreach (var p in parsed)
        {
            string path = $"{Folder}/{FileName(p.Name)}.asset";
            var perk = AssetDatabase.LoadAssetAtPath<StatPerkSO>(path);
            bool isNew = perk == null;
            if (isNew) perk = CreateInstance<StatPerkSO>();

            perk.displayName = p.Name;
            perk.id = Id(p.Name);
            perk.description = p.Description;
            perk.modifiers = p.Modifiers;

            if (isNew) { AssetDatabase.CreateAsset(perk, path); created++; }
            else { EditorUtility.SetDirty(perk); updated++; }
        }

        AssetDatabase.SaveAssets();
        Reparse();
        Debug.Log($"[StatPerkBatch] {Folder}: {created} created, {updated} updated.");
    }

    // ------------------------------------------------------------------ helpers

    // One modifier as a line fragment, writing only non-default options.
    private string Describe(ModifierEntry e)
    {
        if (e == null || e.stat == null) return "<missing stat>";

        var sb = new StringBuilder();
        sb.Append(string.IsNullOrEmpty(e.stat.id) ? Key(e.stat.name) : e.stat.id).Append(' ');
        sb.Append(e.bucket == ModifierBucket.Additive ? "add" : e.bucket == ModifierBucket.Multiplicative ? "mult" : "flat").Append(' ');
        sb.Append(e.value.ToString(CultureInfo.InvariantCulture));

        bool player = e.target == ModifierTarget.Player;
        if (player != DefaultPlayer) sb.Append(player ? " player" : " weapons");
        if (e.scope == ActivityScope.InHands) sb.Append(" hands");

        if (e.weaponTypes != null && e.weaponTypes.Count > 0)
        {
            sb.Append(" types=");
            for (int i = 0; i < e.weaponTypes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var t = e.weaponTypes[i];
                sb.Append(t == null ? "?" : string.IsNullOrEmpty(t.id) ? Key(t.name) : t.id);
            }
        }
        return sb.ToString();
    }

    // Stats and weapon types by id and by asset name, both normalised.
    private void EnsureLookups()
    {
        if (stats == null)
        {
            stats = new Dictionary<string, StatDefinitionSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:StatDefinitionSO"))
            {
                var s = AssetDatabase.LoadAssetAtPath<StatDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (s == null) continue;
                if (!string.IsNullOrEmpty(s.id)) stats[Key(s.id)] = s;
                stats[Key(s.name)] = s;
            }
        }

        if (types == null)
        {
            types = new Dictionary<string, WeaponTypeSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponTypeSO"))
            {
                var t = AssetDatabase.LoadAssetAtPath<WeaponTypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null) continue;
                if (!string.IsNullOrEmpty(t.id)) types[Key(t.id)] = t;
                types[Key(t.name)] = t;
            }
        }
    }

    // "Reload Time", "reload_time" and "ReloadTime" all become "reloadtime".
    private static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static ModifierEntry Clone(ModifierEntry e) => new ModifierEntry
    {
        stat = e.stat,
        bucket = e.bucket,
        value = e.value,
        target = e.target,
        weaponTypes = new List<WeaponTypeSO>(e.weaponTypes),
        scope = e.scope
    };

    private static string FileName(string name)
    {
        var s = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        return s;
    }

    private static string Id(string name) => FileName(name).ToLowerInvariant().Replace(' ', '_');

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string path = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = path + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, parts[i]);
            path = next;
        }
    }
}