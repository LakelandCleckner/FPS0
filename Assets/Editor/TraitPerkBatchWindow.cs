using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Combat.Loadout;
using Combat.Perks;
using Combat.Stats;
using Combat.Weapons;

// Creates and updates trait perks (any perk type except Stat and Effect) from one line of text per perk.
//   Name | type key=value ... | stat bucket value [player|weapons] [hands|loadout] [types=A,B] ; ... | description
// Keys are the type's own fields, so new perk types work without changes here. Only written fields are set.
public class TraitPerkBatchWindow : EditorWindow
{
    private enum Kind { Weapon, Armor }

    private sealed class PerkType
    {
        public Type Type;
        public string Name;                                   // "triggeredbuff"
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();   // key -> property path
        public FieldInfo Modifiers;
        public ScriptableObject Defaults;
    }

    private sealed class Parsed
    {
        public string Name;
        public PerkType Type;
        public readonly List<(string path, string value)> Values = new List<(string, string)>();
        public List<ModifierEntry> Modifiers;                 // null = leave as is
        public string Description;                            // null = leave as is
        public string Path;
        public bool Exists;
    }

    private const string Root = "Assets/GameData/Perks";
    private static readonly HashSet<string> BaseFields = new HashSet<string> { "id", "displayName", "description", "icon", "shortName", "modifiers" };
    private static readonly Regex OptionPattern = new Regex(@"(\w+)\s*=\s*(.*?)(?=\s+\w+\s*=|$)");

    [SerializeField] private Kind kind = Kind.Armor;
    [SerializeField] private string subfolder = "Trait";
    [SerializeField] private string text = "";

    private Vector2 scroll;
    private readonly List<Parsed> parsed = new List<Parsed>();
    private readonly List<string> errors = new List<string>();
    private Dictionary<string, PerkType> types;
    private Dictionary<string, StatDefinitionSO> stats;
    private Dictionary<string, WeaponTypeSO> weaponTypes;
    private readonly Dictionary<string, PerkSO> existingPerks = new Dictionary<string, PerkSO>();

    [MenuItem("Tools/Trait Perk Batch")]
    private static void Open() => GetWindow<TraitPerkBatchWindow>("Trait Perks");

    private void OnEnable() => Reparse();

    private void OnDisable()
    {
        if (types == null) return;
        foreach (var t in types.Values) if (t.Defaults != null) DestroyImmediate(t.Defaults);
        types = null;
    }

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

        EnsureLookups();
        EditorGUILayout.LabelField("Folder", Folder, EditorStyles.miniLabel);
        EditorGUILayout.HelpBox(
            "Name | type key=value ... | stat bucket value [player|weapons] [hands|loadout] [types=A,B] ; ... | description\n" +
            "Types: " + string.Join(", ", types.Keys) + "\n" +
            "Keys are the type's fields (trigger=kill everyN=3 sources=direct,statustick). Default target: " +
            (DefaultPlayer ? "player" : "weapons") + ". Leave a section empty to keep it. # lines are ignored.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load Folder")) Load();
            if (GUILayout.Button("Refresh Assets")) { OnDisable(); stats = null; weaponTypes = null; Reparse(); }
        }

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        EditorGUI.BeginChangeCheck();
        text = EditorGUILayout.TextArea(text, GUILayout.ExpandHeight(true));
        if (EditorGUI.EndChangeCheck()) Reparse();
        EditorGUILayout.EndScrollView();

        if (errors.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Error);
        else if (parsed.Count > 0) EditorGUILayout.HelpBox(Preview(), MessageType.Info);

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
            sb.Append(p.Exists ? "~ " : "+ ").Append(p.Name).Append(" (").Append(p.Type.Name).Append(')');
            foreach (var v in p.Values) sb.Append("  ").Append(v.path).Append('=').Append(v.value);
            if (p.Modifiers != null)
                foreach (var m in p.Modifiers) sb.Append("  [").Append(Describe(m)).Append(']');
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
        IndexFolder();

        var lines = (text ?? "").Replace("\r", "").Split('\n');
        var names = new HashSet<string>();
        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            try
            {
                var p = ParseLine(line);
                if (!names.Add(Key(p.Name))) throw new FormatException($"duplicate name '{p.Name}'");
                parsed.Add(p);
            }
            catch (FormatException e)
            {
                errors.Add($"Line {n + 1}: {e.Message}");
            }
        }
    }

    private Parsed ParseLine(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 2) throw new FormatException("expected 'Name | type ...'");

        var p = new Parsed { Name = parts[0].Trim() };
        if (p.Name.Length == 0) throw new FormatException("missing name");

        // type, then key=value options
        string head = parts[1].Trim();
        int space = head.IndexOf(' ');
        string typeName = space < 0 ? head : head.Substring(0, space);
        if (!types.TryGetValue(Key(typeName), out p.Type))
            throw new FormatException($"unknown type '{typeName}' ({string.Join(", ", types.Keys)})");

        string opts = space < 0 ? "" : head.Substring(space + 1).Trim();
        if (opts.Length > 0)
        {
            var matches = OptionPattern.Matches(opts);
            if (matches.Count == 0 || matches[0].Index != 0) throw new FormatException($"expected key=value, got '{opts}'");
            foreach (Match m in matches)
            {
                string key = m.Groups[1].Value;
                if (!p.Type.Fields.TryGetValue(Key(key), out var path))
                    throw new FormatException($"{p.Type.Name} has no field '{key}' ({string.Join(", ", p.Type.Fields.Values)})");
                string value = m.Groups[2].Value.Trim();
                Validate(p.Type, path, value);
                p.Values.Add((path, value));
            }
        }

        if (parts.Length > 2 && parts[2].Trim().Length > 0)
        {
            if (p.Type.Modifiers == null) throw new FormatException($"{p.Type.Name} has no modifiers");
            p.Modifiers = new List<ModifierEntry>();
            if (parts[2].Trim() != "-")
                foreach (var raw in parts[2].Split(';'))
                    if (raw.Trim().Length > 0) p.Modifiers.Add(ParseModifier(raw.Trim()));
        }

        if (parts.Length > 3) p.Description = string.Join("|", parts, 3, parts.Length - 3).Trim();

        // An existing perk in the folder with this name, id or file name is updated where it lives.
        existingPerks.TryGetValue(Key(p.Name), out var existing);
        p.Exists = existing != null;
        p.Path = p.Exists ? AssetDatabase.GetAssetPath(existing) : $"{Folder}/{FileName(p.Name)}.asset";
        if (p.Exists && existing.GetType() != p.Type.Type)
            throw new FormatException($"'{p.Name}' already exists as a {existing.GetType().Name}");
        return p;
    }

    // Checks a value parses for its property, without writing.
    private static void Validate(PerkType t, string path, string value)
    {
        var so = new SerializedObject(t.Defaults);
        var prop = so.FindProperty(path);
        Write(prop, FieldOf(t.Type, path), value);
    }

    private ModifierEntry ParseModifier(string text)
    {
        var tok = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 3) throw new FormatException($"'{text}': expected 'stat bucket value'");
        if (!stats.TryGetValue(Key(tok[0]), out var stat)) throw new FormatException($"unknown stat '{tok[0]}'");

        var e = new ModifierEntry
        {
            stat = stat,
            bucket = ParseBucket(tok[1]),
            target = DefaultPlayer ? ModifierTarget.Player : ModifierTarget.Weapons,
            scope = ActivityScope.InLoadout
        };
        if (!float.TryParse(tok[2], NumberStyles.Float, CultureInfo.InvariantCulture, out e.value))
            throw new FormatException($"'{tok[2]}' is not a number");

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
                        if (!weaponTypes.TryGetValue(Key(name), out var type)) throw new FormatException($"unknown weapon type '{name}'");
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

    // ------------------------------------------------------------------ values

    // Writes a text value into a property: float, int, bool, enum or flags enum.
    private static void Write(SerializedProperty prop, FieldInfo field, string value)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.Float:
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                    throw new FormatException($"{prop.name}: '{value}' is not a number");
                prop.floatValue = f;
                break;
            case SerializedPropertyType.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                    throw new FormatException($"{prop.name}: '{value}' is not a whole number");
                prop.intValue = i;
                break;
            case SerializedPropertyType.Boolean:
                switch (Key(value))
                {
                    case "true": case "yes": case "on": case "1": prop.boolValue = true; break;
                    case "false": case "no": case "off": case "0": prop.boolValue = false; break;
                    default: throw new FormatException($"{prop.name}: '{value}' is not true or false");
                }
                break;
            case SerializedPropertyType.Enum:
                var enumType = field.FieldType;
                bool flags = enumType.IsDefined(typeof(FlagsAttribute), false);
                int result = 0;
                foreach (var raw in value.Split(','))
                {
                    string name = raw.Trim();
                    if (name.Length == 0) continue;
                    int v = EnumValue(enumType, name, prop.name);
                    if (!flags) { result = v; break; }
                    result |= v;
                }
                if (flags) prop.enumValueFlag = result;
                else prop.enumValueIndex = Array.IndexOf(Enum.GetValues(enumType), Enum.ToObject(enumType, result));
                break;
            default:
                throw new FormatException($"{prop.name} can't be set from text; edit it in the inspector");
        }
    }

    private static int EnumValue(Type enumType, string name, string field)
    {
        foreach (var n in Enum.GetNames(enumType))
            if (Key(n) == Key(name)) return Convert.ToInt32(Enum.Parse(enumType, n));
        throw new FormatException($"{field}: '{name}' is not one of {string.Join(", ", Enum.GetNames(enumType))}");
    }

    // A property's value as text, for Load.
    private static string Read(SerializedProperty prop, FieldInfo field)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.Float: return prop.floatValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Integer: return prop.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return prop.boolValue ? "true" : "false";
            case SerializedPropertyType.Enum:
                var enumType = field.FieldType;
                if (!enumType.IsDefined(typeof(FlagsAttribute), false))
                    return Enum.GetNames(enumType)[Mathf.Clamp(prop.enumValueIndex, 0, Enum.GetNames(enumType).Length - 1)].ToLowerInvariant();
                int flags = prop.enumValueFlag;
                var names = new List<string>();
                foreach (var n in Enum.GetNames(enumType))
                {
                    int v = Convert.ToInt32(Enum.Parse(enumType, n));
                    if (v != 0 && (v & (v - 1)) == 0 && (flags & v) == v) names.Add(n.ToLowerInvariant());
                }
                return names.Count > 0 ? string.Join(",", names) : "none";
            default: return null;
        }
    }

    private static FieldInfo FieldOf(Type type, string path)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(path, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }

    // ------------------------------------------------------------------ apply

    private void Apply()
    {
        EnsureFolder(Folder);
        int created = 0, updated = 0;

        foreach (var p in parsed)
        {
            var perk = AssetDatabase.LoadAssetAtPath<PerkSO>(p.Path);
            bool isNew = perk == null;
            if (isNew)
            {
                perk = (PerkSO)CreateInstance(p.Type.Type);
                CallReset(perk);   // code-created assets skip Reset, which holds type defaults like listen scope
            }
            else Undo.RecordObject(perk, "Trait Perk Batch");

            perk.displayName = p.Name;
            if (string.IsNullOrEmpty(perk.id)) perk.id = Id(p.Name);
            if (p.Description != null) perk.description = p.Description;
            if (p.Modifiers != null) p.Type.Modifiers.SetValue(perk, new List<ModifierEntry>(p.Modifiers));

            var so = new SerializedObject(perk);
            foreach (var v in p.Values) Write(so.FindProperty(v.path), FieldOf(p.Type.Type, v.path), v.value);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (isNew) { AssetDatabase.CreateAsset(perk, p.Path); created++; }
            else { EditorUtility.SetDirty(perk); updated++; }
        }

        AssetDatabase.SaveAssets();
        Reparse();
        Debug.Log($"[TraitPerkBatch] {Folder}: {created} created, {updated} updated.");
    }

    private static void CallReset(ScriptableObject o)
    {
        for (var t = o.GetType(); t != null && t != typeof(ScriptableObject); t = t.BaseType)
        {
            var m = t.GetMethod("Reset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                                null, Type.EmptyTypes, null);
            if (m != null) { m.Invoke(o, null); return; }
        }
    }

    // ------------------------------------------------------------------ load

    // The folder's trait perks back into lines, writing only fields that differ from the type's defaults.
    private void Load()
    {
        var sb = new StringBuilder();
        if (AssetDatabase.IsValidFolder(Folder))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:PerkSO", new[] { Folder }))
            {
                var perk = AssetDatabase.LoadAssetAtPath<PerkSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (perk == null) continue;
                PerkType pt = null;
                foreach (var t in types.Values) if (t.Type == perk.GetType()) pt = t;
                if (pt == null) continue;

                sb.Append(string.IsNullOrEmpty(perk.displayName) ? perk.name : perk.displayName).Append(" | ").Append(pt.Name);

                var so = new SerializedObject(perk);
                var def = new SerializedObject(pt.Defaults);
                foreach (var path in pt.Fields.Values)
                {
                    var field = FieldOf(pt.Type, path);
                    string v = Read(so.FindProperty(path), field);
                    if (v != null && v != Read(def.FindProperty(path), field)) sb.Append(' ').Append(path).Append('=').Append(v);
                }

                sb.Append(" | ");
                if (pt.Modifiers != null && pt.Modifiers.GetValue(perk) is List<ModifierEntry> mods)
                    for (int i = 0; i < mods.Count; i++) sb.Append(i > 0 ? " ; " : "").Append(Describe(mods[i]));

                if (!string.IsNullOrWhiteSpace(perk.description)) sb.Append(" | ").Append(perk.description.Replace("\n", " "));
                sb.Append('\n');
            }
        }
        text = sb.ToString();
        GUI.FocusControl(null);
        Reparse();
    }

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
                sb.Append(i > 0 ? "," : "").Append(e.weaponTypes[i] == null ? "?" : string.IsNullOrEmpty(e.weaponTypes[i].id) ? Key(e.weaponTypes[i].name) : e.weaponTypes[i].id);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ lookups

    private void EnsureLookups()
    {
        if (types == null)
        {
            types = new Dictionary<string, PerkType>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<PerkSO>())
            {
                if (t.IsAbstract || t == typeof(StatPerkSO) || t.Name == "EffectPerkSO") continue;
                var pt = new PerkType { Type = t, Name = Key(t.Name.EndsWith("PerkSO") ? t.Name.Substring(0, t.Name.Length - 6) : t.Name) };
                pt.Defaults = CreateInstance(t);
                pt.Defaults.hideFlags = HideFlags.HideAndDontSave;
                CallReset(pt.Defaults);

                var mf = FieldOf(t, "modifiers");
                if (mf != null && mf.FieldType == typeof(List<ModifierEntry>)) pt.Modifiers = mf;

                var it = new SerializedObject(pt.Defaults).GetIterator();
                for (bool enter = true; it.NextVisible(enter); enter = false)
                    if (it.name != "m_Script" && !BaseFields.Contains(it.name)) pt.Fields[Key(it.name)] = it.name;

                types[pt.Name] = pt;
            }
        }

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

        if (weaponTypes == null)
        {
            weaponTypes = new Dictionary<string, WeaponTypeSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponTypeSO"))
            {
                var t = AssetDatabase.LoadAssetAtPath<WeaponTypeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null) continue;
                if (!string.IsNullOrEmpty(t.id)) weaponTypes[Key(t.id)] = t;
                weaponTypes[Key(t.name)] = t;
            }
        }
    }

    private void IndexFolder()
    {
        existingPerks.Clear();
        if (!AssetDatabase.IsValidFolder(Folder)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:PerkSO", new[] { Folder }))
        {
            var perk = AssetDatabase.LoadAssetAtPath<PerkSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (perk == null) continue;
            foreach (var k in new[] { perk.displayName, perk.id, perk.name })
                if (!string.IsNullOrWhiteSpace(k) && !existingPerks.ContainsKey(Key(k))) existingPerks[Key(k)] = perk;
        }
    }

    // ------------------------------------------------------------------ helpers

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
