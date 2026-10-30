using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Perks;
using Combat.Weapons;
using Object = UnityEngine.Object;

// Authors perk pools and assigns them to roll columns, as text.
//   Pools:        Name | Perk, Prefix*, @OtherPool, folder=Sub, -Perk
//   Assignments:  Target | Pool, Pool, -, Pool [| Label, Label]   or   Target | inherit
//   Target:       an item name, or type=WeaponType / slot=ArmorSlot for shared defaults.
public class PerkPoolBatchWindow : EditorWindow
{
    private enum Kind { Weapon, Armor }

    private sealed class ParsedPool
    {
        public string Name;
        public string Body;
        public readonly List<PerkSO> Perks = new List<PerkSO>();
        public readonly List<string> Includes = new List<string>();
    }

    private sealed class ParsedAssignment
    {
        public string Label;            // for the preview
        public Object Target;
        public bool Inherit;
        public readonly List<string> Pools = new List<string>();   // "" = no pool
        public readonly List<string> Labels = new List<string>();
    }

    private const string PerkRoot = "Assets/GameData/Perks";

    [SerializeField] private Kind kind = Kind.Weapon;
    [SerializeField] private string poolText = "";
    [SerializeField] private string assignText = "";

    private Vector2 poolScroll, assignScroll;
    private readonly List<ParsedPool> pools = new List<ParsedPool>();
    private readonly List<ParsedAssignment> assignments = new List<ParsedAssignment>();
    private readonly List<string> errors = new List<string>();

    private Dictionary<string, PerkSO> perkByKey;
    private List<(string name, PerkSO perk)> perkNames;
    private HashSet<string> ambiguousPerks;
    private Dictionary<string, PerkPoolSO> poolAssets;
    private Dictionary<string, Object> items, groups;

    [MenuItem("Tools/Perk Pool Batch")]
    private static void Open() => GetWindow<PerkPoolBatchWindow>("Perk Pools");

    private void OnEnable() => Reparse();

    private string PerkFolder => $"{PerkRoot}/{kind}";
    private string PoolFolder => $"{PerkRoot}/Pools/{kind}";
    private string GroupWord => kind == Kind.Weapon ? "type" : "slot";

    // ------------------------------------------------------------------ UI

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        kind = (Kind)EditorGUILayout.EnumPopup("Perks For", kind);
        if (EditorGUI.EndChangeCheck()) { ClearLookups(); Reparse(); }

        EditorGUILayout.LabelField("Perks from", PerkFolder, EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Pools saved to", PoolFolder, EditorStyles.miniLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load Current")) Load();
            if (GUILayout.Button("Refresh Assets")) { ClearLookups(); Reparse(); }
        }

        EditorGUILayout.LabelField("Pools", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Name | Perk, Prefix*, @OtherPool, folder=Sub, -Perk\n" +
            "* matches names starting with the text. folder=Trait adds every perk in Perks/" + kind + "/Trait. -Perk removes one.\n" +
            "Folder rules are re-read on every Apply, so re-apply after adding perks.",
            MessageType.None);
        EditorGUI.BeginChangeCheck();
        poolText = TextBox(poolText, ref poolScroll);
        if (EditorGUI.EndChangeCheck()) Reparse();

        EditorGUILayout.LabelField("Assignments", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"Item | Pool, Pool, -, Pool | Label, Label     ('-' = no pool for that column)\n" +
            $"{GroupWord}=Name | ...  sets the default for every item without its own columns.\n" +
            "Item | inherit  clears an item's own columns so it uses the default.",
            MessageType.None);
        EditorGUI.BeginChangeCheck();
        assignText = TextBox(assignText, ref assignScroll);
        if (EditorGUI.EndChangeCheck()) Reparse();

        if (errors.Count > 0)
            EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Error);
        else if (pools.Count + assignments.Count > 0)
            EditorGUILayout.HelpBox(Preview(), MessageType.Info);

        using (new EditorGUI.DisabledScope(errors.Count > 0 || pools.Count + assignments.Count == 0))
        {
            if (GUILayout.Button($"Apply {pools.Count} Pool(s), {assignments.Count} Assignment(s)", GUILayout.Height(28)))
                Apply();
        }
    }

    private static string TextBox(string value, ref Vector2 scroll)
    {
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(120), GUILayout.ExpandHeight(true));
        value = EditorGUILayout.TextArea(value, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        return value;
    }

    private string Preview()
    {
        var sb = new StringBuilder();
        foreach (var p in pools)
        {
            sb.Append("Pool ").Append(p.Name).Append(": ").Append(p.Perks.Count).Append(" perk(s)");
            if (p.Includes.Count > 0) sb.Append(" + ").Append(string.Join(", ", p.Includes));
            sb.Append('\n');
        }
        foreach (var a in assignments)
        {
            sb.Append(a.Label).Append(": ");
            sb.Append(a.Inherit ? "inherit" : string.Join(", ", a.Pools.ConvertAll(x => x.Length == 0 ? "-" : x)));
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ parse

    private void Reparse()
    {
        pools.Clear();
        assignments.Clear();
        errors.Clear();
        EnsureLookups();

        var poolNames = new HashSet<string>();

        ForEachLine(poolText, "Pools", (line, where) =>
        {
            var parts = line.Split('|');
            if (parts.Length < 2) throw new FormatException("expected 'Name | perks'");

            var p = new ParsedPool { Name = parts[0].Trim(), Body = string.Join("|", parts, 1, parts.Length - 1).Trim() };
            var excludes = new List<PerkSO>();
            if (p.Name.Length == 0) throw new FormatException("missing name");
            if (!poolNames.Add(Key(p.Name))) throw new FormatException($"duplicate pool '{p.Name}'");

            foreach (var raw in parts[1].Split(','))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;

                if (item.StartsWith("@")) { p.Includes.Add(item.Substring(1).Trim()); continue; }

                if (item.StartsWith("folder=", StringComparison.OrdinalIgnoreCase))
                {
                    string sub = item.Substring(7).Trim().Trim('/');
                    string path = PerkFolder + "/" + sub;
                    if (!AssetDatabase.IsValidFolder(path)) throw new FormatException($"no folder '{path}'");

                    var found = FindAll<PerkSO>(path);
                    if (found.Count == 0) throw new FormatException($"'{path}' has no perks");
                    found.Sort((a, b) => string.CompareOrdinal(PerkName(a), PerkName(b)));
                    foreach (var f in found) if (!p.Perks.Contains(f)) p.Perks.Add(f);
                    continue;
                }

                if (item.StartsWith("-"))
                {
                    excludes.Add(ResolvePerk(item.Substring(1).Trim()));
                    continue;
                }

                if (item.EndsWith("*"))
                {
                    string prefix = item.TrimEnd('*').Trim();
                    int before = p.Perks.Count;
                    foreach (var e in perkNames)
                        if (e.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !p.Perks.Contains(e.perk))
                            p.Perks.Add(e.perk);
                    if (p.Perks.Count == before) throw new FormatException($"'{item}' matches no perks");
                    continue;
                }

                var perk = ResolvePerk(item);
                if (!p.Perks.Contains(perk)) p.Perks.Add(perk);
            }

            foreach (var x in excludes) p.Perks.Remove(x);
            if (p.Perks.Count == 0 && p.Includes.Count == 0) throw new FormatException("pool is empty");
            pools.Add(p);
        });

        // Includes can point at pools defined above or already saved.
        foreach (var p in pools)
            foreach (var inc in p.Includes)
                if (!PoolExists(inc, poolNames)) errors.Add($"Pools: '{p.Name}' includes unknown pool '{inc}'");

        var targets = new HashSet<Object>();

        ForEachLine(assignText, "Assignments", (line, where) =>
        {
            var parts = line.Split('|');
            if (parts.Length < 2) throw new FormatException("expected 'Item | pools'");

            string target = parts[0].Trim();
            var a = new ParsedAssignment { Label = target };

            string prefix = GroupWord + "=";
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!groups.TryGetValue(Key(target.Substring(prefix.Length)), out a.Target))
                    throw new FormatException($"unknown {GroupWord} '{target.Substring(prefix.Length)}'");
            }
            else if (!items.TryGetValue(Key(target), out a.Target))
                throw new FormatException($"unknown {kind.ToString().ToLowerInvariant()} '{target}'");

            if (!targets.Add(a.Target)) throw new FormatException($"'{target}' is assigned twice");

            string body = parts[1].Trim();
            if (body.Equals("inherit", StringComparison.OrdinalIgnoreCase))
            {
                if (a.Target is WeaponTypeSO || a.Target is ArmorSlotSO)
                    throw new FormatException($"only items can inherit, not a {GroupWord}");
                a.Inherit = true;
            }
            else
            {
                foreach (var raw in body.Split(','))
                {
                    string name = raw.Trim();
                    if (name == "-") { a.Pools.Add(""); continue; }
                    if (name.Length == 0) continue;
                    if (!PoolExists(name, poolNames)) throw new FormatException($"unknown pool '{name}'");
                    a.Pools.Add(name);
                }
                if (a.Pools.Count == 0) throw new FormatException("no columns");

                if (parts.Length > 2)
                    foreach (var l in parts[2].Split(',')) a.Labels.Add(l.Trim());
            }
            assignments.Add(a);
        });
    }

    private void ForEachLine(string text, string section, Action<string, string> parse)
    {
        var lines = (text ?? "").Replace("\r", "").Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            try { parse(line, section); }
            catch (FormatException e) { errors.Add($"{section} line {n + 1}: {e.Message}"); }
        }
    }

    private PerkSO ResolvePerk(string name)
    {
        string k = Key(name);
        if (ambiguousPerks.Contains(k)) throw new FormatException($"more than one perk is named '{name}'");
        if (!perkByKey.TryGetValue(k, out var found)) throw new FormatException($"unknown perk '{name}'");
        return found;
    }

    private bool PoolExists(string name, HashSet<string> defined)
        => defined.Contains(Key(name)) || poolAssets.ContainsKey(Key(name));

    // ------------------------------------------------------------------ apply

    private void Apply()
    {
        EnsureFolder(PoolFolder);

        // Create every pool first so includes and assignments can reference them.
        var made = new Dictionary<string, PerkPoolSO>(poolAssets);
        int created = 0, updated = 0;

        foreach (var p in pools)
        {
            string path = $"{PoolFolder}/{FileName(p.Name)}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<PerkPoolSO>(path);
            if (asset == null)
            {
                asset = CreateInstance<PerkPoolSO>();
                AssetDatabase.CreateAsset(asset, path);
                created++;
            }
            else updated++;
            made[Key(p.Name)] = asset;
        }

        foreach (var p in pools)
        {
            var asset = made[Key(p.Name)];
            asset.perks = new List<PerkSO>(p.Perks);
            asset.includes = p.Includes.ConvertAll(n => made[Key(n)]);
            asset.authoredAs = p.Body;
            EditorUtility.SetDirty(asset);
        }

        foreach (var a in assignments)
        {
            var current = ColumnsOf(a.Target);
            var next = new List<PerkColumn>();

            if (!a.Inherit)
            {
                for (int i = 0; i < a.Pools.Count; i++)
                {
                    var old = current != null && i < current.Count ? current[i] : null;
                    next.Add(new PerkColumn
                    {
                        label = i < a.Labels.Count && a.Labels[i].Length > 0 ? a.Labels[i] : old != null ? old.label : "",
                        source = a.Pools[i].Length > 0 ? made[Key(a.Pools[i])] : null,
                        extraPerks = old != null ? old.extraPerks : new List<PerkSO>()   // keeps one-off extras
                    });
                }
            }

            SetColumns(a.Target, next);
            EditorUtility.SetDirty(a.Target);
        }

        AssetDatabase.SaveAssets();
        ClearLookups();
        Reparse();
        Debug.Log($"[PerkPoolBatch] Pools: {created} created, {updated} updated. Assignments: {assignments.Count}.");
    }

    // ------------------------------------------------------------------ load

    private void Load()
    {
        EnsureLookups();

        var sb = new StringBuilder();
        foreach (var pool in FindAll<PerkPoolSO>(PoolFolder))
        {
            sb.Append(pool.name).Append(" | ");
            if (!string.IsNullOrWhiteSpace(pool.authoredAs))
            {
                sb.Append(pool.authoredAs.Trim()).Append('\n');
                continue;
            }
            var parts = new List<string>();
            foreach (var p in pool.perks) if (p != null) parts.Add(PerkName(p));
            foreach (var inc in pool.includes) if (inc != null) parts.Add("@" + inc.name);
            sb.Append(string.Join(", ", parts)).Append('\n');
        }
        poolText = sb.ToString();

        sb.Clear();
        if (kind == Kind.Weapon)
        {
            foreach (var t in FindAll<WeaponTypeSO>(null)) AppendAssignment(sb, "type=" + t.name, t.perkColumns);
            foreach (var w in FindAll<WeaponSO>(null)) AppendAssignment(sb, w.name, w.perkColumns);
        }
        else
        {
            foreach (var s in FindAll<ArmorSlotSO>(null)) AppendAssignment(sb, "slot=" + s.name, s.perkColumns);
            foreach (var a in FindAll<ArmorSO>(null)) AppendAssignment(sb, a.name, a.perkColumns);
        }
        assignText = sb.ToString();

        GUI.FocusControl(null);
        Reparse();
    }

    private static void AppendAssignment(StringBuilder sb, string target, List<PerkColumn> cols)
    {
        if (cols == null || cols.Count == 0) return;

        var names = new List<string>();
        var labels = new List<string>();
        bool anyLabel = false;
        foreach (var c in cols)
        {
            names.Add(c != null && c.source != null ? c.source.name : "-");
            string l = c != null ? c.label : "";
            labels.Add(l);
            anyLabel |= !string.IsNullOrEmpty(l);
        }

        sb.Append(target).Append(" | ").Append(string.Join(", ", names));
        if (anyLabel) sb.Append(" | ").Append(string.Join(", ", labels));
        sb.Append('\n');
    }

    // ------------------------------------------------------------------ lookups

    private void ClearLookups() => perkByKey = null;

    private void EnsureLookups()
    {
        if (perkByKey != null) return;

        perkByKey = new Dictionary<string, PerkSO>();
        perkNames = new List<(string, PerkSO)>();
        ambiguousPerks = new HashSet<string>();

        if (AssetDatabase.IsValidFolder(PerkFolder))
        {
            foreach (var perk in FindAll<PerkSO>(PerkFolder))
            {
                string display = PerkName(perk);
                perkNames.Add((display, perk));
                if (!Key(display).Equals(Key(perk.name))) perkNames.Add((perk.name, perk));

                AddPerkKey(Key(display), perk);
                AddPerkKey(Key(perk.name), perk);
                if (!string.IsNullOrEmpty(perk.id)) AddPerkKey(Key(perk.id), perk);
            }
        }

        poolAssets = new Dictionary<string, PerkPoolSO>();
        if (AssetDatabase.IsValidFolder(PoolFolder))
            foreach (var p in FindAll<PerkPoolSO>(PoolFolder)) poolAssets[Key(p.name)] = p;

        items = new Dictionary<string, Object>();
        groups = new Dictionary<string, Object>();
        if (kind == Kind.Weapon)
        {
            foreach (var w in FindAll<WeaponSO>(null)) AddNames(items, w, w.id, w.displayName);
            foreach (var t in FindAll<WeaponTypeSO>(null)) AddNames(groups, t, t.id, t.displayName);
        }
        else
        {
            foreach (var a in FindAll<ArmorSO>(null)) AddNames(items, a, a.id, a.displayName);
            foreach (var s in FindAll<ArmorSlotSO>(null)) AddNames(groups, s, s.id, s.displayName);
        }
    }

    private void AddPerkKey(string key, PerkSO perk)
    {
        if (key.Length == 0) return;
        if (perkByKey.TryGetValue(key, out var existing) && existing != perk) ambiguousPerks.Add(key);
        else perkByKey[key] = perk;
    }

    private static void AddNames(Dictionary<string, Object> map, Object o, string id, string display)
    {
        map[Key(o.name)] = o;
        if (!string.IsNullOrEmpty(id)) map[Key(id)] = o;
        if (!string.IsNullOrEmpty(display)) map[Key(display)] = o;
    }

    private static string PerkName(PerkSO p) => string.IsNullOrEmpty(p.displayName) ? p.name : p.displayName;

    private static List<T> FindAll<T>(string folder) where T : Object
    {
        var list = new List<T>();
        var guids = folder != null
            ? AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder })
            : AssetDatabase.FindAssets("t:" + typeof(T).Name);
        foreach (var g in guids)
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
            if (a != null) list.Add(a);
        }
        return list;
    }

    private static List<PerkColumn> ColumnsOf(Object o)
    {
        if (o is WeaponSO w) return w.perkColumns;
        if (o is WeaponTypeSO t) return t.perkColumns;
        if (o is ArmorSO a) return a.perkColumns;
        if (o is ArmorSlotSO s) return s.perkColumns;
        return null;
    }

    private static void SetColumns(Object o, List<PerkColumn> cols)
    {
        if (o is WeaponSO w) w.perkColumns = cols;
        else if (o is WeaponTypeSO t) t.perkColumns = cols;
        else if (o is ArmorSO a) a.perkColumns = cols;
        else if (o is ArmorSlotSO s) s.perkColumns = cols;
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

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}