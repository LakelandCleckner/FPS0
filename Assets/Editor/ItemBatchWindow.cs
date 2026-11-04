using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Core;
using Combat.Perks;
using Combat.Weapons;

// Creates and updates armor, weapon and archetype assets from one line of text per item.
//   Armor:     Name[, Name] | slot [| intrinsic=Perk, Perk]
//   Weapons:    Name[, Name] | archetype [| key=value ...]
//   Archetypes: Name[, Name] | type [| key=value ...]
// Only fields written in a line are set, so updating an existing asset keeps everything else.
// Roll columns are assigned with Perk Pool Batch.
public class ItemBatchWindow : EditorWindow
{
    private enum Kind { Armor, Weapon, Archetype }

    private sealed class Parsed
    {
        public string Name;
        public UnityEngine.Object Group;      // ArmorSlotSO, WeaponArchetypeSO or WeaponTypeSO
        public readonly Dictionary<string, string> Options = new Dictionary<string, string>();
        public List<PerkSO> Intrinsic;        // null = leave as is
        public ScriptableObject From;         // WeaponSO or WeaponArchetypeSO
        public DamageTypeSO Element;
        public FireBehaviorSO Behavior;
        public DeliverySO Delivery;
        public AmmoPoolSO Pool;
        public bool ClearPool;
        public string Path;
        public bool Exists;
    }

    private const string ArmorRoot = "Assets/GameData/Armor/Pieces";
    private const string WeaponRoot = "Assets/GameData/Weapons";

    private static readonly string[] DeltaKeys = { "damage", "rpm", "mag", "reload", "handling", "zoom", "range", "aa" };
    private static readonly string[] ArchNumberKeys = { "damage", "rpm", "mag", "reload", "handling", "zoom", "range", "aa", "ads", "equip", "stow" };
    private static readonly HashSet<string> ArmorKeys = new HashSet<string> { "intrinsic" };
    private static readonly HashSet<string> WeaponKeys = new HashSet<string>(DeltaKeys) { "intrinsic", "from", "element", "reserves" };
    private static readonly HashSet<string> ArchKeys = new HashSet<string>(ArchNumberKeys) { "from", "behavior", "delivery", "reloadstyle", "infinite", "pool" };
    private static readonly Regex OptionPattern = new Regex(@"(\w+)\s*=\s*(.*?)(?=\s+\w+\s*=|$)");

    [SerializeField] private Kind kind = Kind.Armor;
    [SerializeField] private string text = "";

    private Vector2 scroll;
    private readonly List<Parsed> parsed = new List<Parsed>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> warnings = new List<string>();

    private Dictionary<string, ArmorSlotSO> slots;
    private Dictionary<string, WeaponTypeSO> types;
    private Dictionary<string, WeaponArchetypeSO> archetypes;
    private Dictionary<string, PerkSO> perks;
    private Dictionary<string, WeaponSO> weapons;
    private Dictionary<string, ArmorSO> armors;
    private Dictionary<string, DamageTypeSO> elements;
    private Dictionary<string, FireBehaviorSO> behaviors;
    private Dictionary<string, DeliverySO> deliveries;
    private Dictionary<string, AmmoPoolSO> pools;

    [MenuItem("Tools/Item Batch")]
    private static void Open() => GetWindow<ItemBatchWindow>("Items");

    private void OnEnable() => Reparse();

    // ------------------------------------------------------------------ UI

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        kind = (Kind)EditorGUILayout.EnumPopup("Items", kind);
        if (EditorGUI.EndChangeCheck()) Reparse();

        EditorGUILayout.HelpBox(Help(), MessageType.None);
        EditorGUILayout.LabelField("Only fields in a line are written. Lines starting with # are ignored.", EditorStyles.miniLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load Existing")) Load();
            if (GUILayout.Button("Refresh Assets")) { ClearLookups(); Reparse(); }
        }

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        EditorGUI.BeginChangeCheck();
        text = EditorGUILayout.TextArea(text, GUILayout.ExpandHeight(true));
        if (EditorGUI.EndChangeCheck()) Reparse();
        EditorGUILayout.EndScrollView();

        if (errors.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Error);
        else if (parsed.Count > 0) EditorGUILayout.HelpBox(Preview(), MessageType.Info);
        if (errors.Count == 0 && warnings.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", warnings), MessageType.Warning);

        using (new EditorGUI.DisabledScope(errors.Count > 0 || parsed.Count == 0))
        {
            if (GUILayout.Button($"Apply {parsed.Count} Item(s)", GUILayout.Height(28))) Apply();
        }
    }

    private string Help()
    {
        switch (kind)
        {
            case Kind.Armor:
                return "Name[, Name] | slot [| intrinsic=Perk, Perk]\n" +
                       "Saved to " + ArmorRoot + "/<Slot>. Roll columns come from the slot unless set in Perk Pool Batch.";
            case Kind.Weapon:
                return "Name[, Name] | archetype [| key=value ...]\n" +
                       "Keys: damage rpm mag reload handling zoom range aa (deltas), reserves, element=Type, from=Weapon, intrinsic=Perk, Perk\n" +
                       "New weapons get their type's default viewmodel and shader. from= copies viewmodel, skin, shader, element, audio and reserves instead.";
            default:
                return "Name[, Name] | type [| key=value ...]\n" +
                       "Keys (absolute values): damage rpm mag reload handling zoom range aa ads equip stow, behavior=, delivery=, reloadstyle=magazine|pershell, infinite=true|false, pool=Pool|none, from=Archetype\n" +
                       "from= copies every other field (falloff, curves, fire mode, crosshair). Saved to Weapons/<Type>/_Archetypes/<Name>.";
        }
    }

    private string Preview()
    {
        int created = 0;
        foreach (var p in parsed) if (!p.Exists) created++;

        var sb = new StringBuilder();
        sb.Append($"{created} new, {parsed.Count - created} update\n");
        int shown = 0;
        foreach (var p in parsed)
        {
            if (shown++ == 12) { sb.Append($"...and {parsed.Count - 12} more"); break; }
            sb.Append(p.Exists ? "~ " : "+ ").Append(p.Name).Append("  ->  ").Append(p.Group.name);
            if (p.Intrinsic != null) sb.Append("  intrinsic ").Append(p.Intrinsic.Count);
            foreach (var kv in p.Options)
                if (kv.Key != "intrinsic") sb.Append("  ").Append(kv.Key).Append('=').Append(kv.Value);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ parse

    private void Reparse()
    {
        parsed.Clear();
        errors.Clear();
        warnings.Clear();
        EnsureLookups();

        var lines = (text ?? "").Replace("\r", "").Split('\n');
        var names = new HashSet<string>();

        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            try
            {
                foreach (var p in ParseLine(line))
                {
                    if (!names.Add(Key(p.Name))) throw new FormatException($"duplicate name '{p.Name}'");
                    parsed.Add(p);
                    Warn(p);
                }
            }
            catch (FormatException e)
            {
                errors.Add($"Line {n + 1}: {e.Message}");
            }
        }
    }

    private void Warn(Parsed p)
    {
        if (p.Exists || p.From != null) return;
        if (kind == Kind.Weapon)
        {
            var type = ((WeaponArchetypeSO)p.Group).weaponType;
            if (type == null || type.defaultViewmodel == null)
                warnings.Add($"{p.Name}: its type has no default viewmodel; add from= or set one on the type.");
        }
        else if (kind == Kind.Archetype && (p.Behavior == null || p.Delivery == null))
            warnings.Add($"{p.Name}: no from= and no behavior/delivery, so it can't fire yet.");
    }

    private IEnumerable<Parsed> ParseLine(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 2) throw new FormatException("expected 'Name | " + GroupWord + "'");

        string group = parts[1].Trim();
        UnityEngine.Object groupAsset = kind == Kind.Armor ? Find(slots, group, "slot")
                                      : kind == Kind.Weapon ? (UnityEngine.Object)Find(archetypes, group, "archetype")
                                      : Find(types, group, "type");

        var options = ParseOptions(parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : "");
        var template = new Parsed { Group = groupAsset };
        foreach (var kv in options) template.Options[kv.Key] = kv.Value;

        if (options.TryGetValue("intrinsic", out var list))
        {
            template.Intrinsic = new List<PerkSO>();
            foreach (var raw in list.Split(','))
            {
                string pn = raw.Trim();
                if (pn.Length == 0 || pn == "-") continue;
                template.Intrinsic.Add(Find(perks, pn, "perk"));
            }
        }

        if (kind == Kind.Weapon)
        {
            if (options.TryGetValue("from", out var f)) template.From = Find(weapons, f, "weapon");
            if (options.TryGetValue("element", out var el)) template.Element = Find(elements, el, "damage type");
            foreach (var k in DeltaKeys) if (options.ContainsKey(k)) Number(options[k], k);
            if (options.ContainsKey("reserves")) Number(options["reserves"], "reserves");
        }
        else if (kind == Kind.Archetype)
        {
            if (options.TryGetValue("from", out var f)) template.From = Find(archetypes, f, "archetype");
            if (options.TryGetValue("behavior", out var b)) template.Behavior = Find(behaviors, b, "fire behavior");
            if (options.TryGetValue("delivery", out var d)) template.Delivery = Find(deliveries, d, "delivery");
            if (options.TryGetValue("pool", out var pl))
            {
                if (Key(pl) == "none") template.ClearPool = true;
                else template.Pool = Find(pools, pl, "ammo pool");
            }
            if (options.TryGetValue("reloadstyle", out var rs)) ParseReloadStyle(rs);
            if (options.TryGetValue("infinite", out var inf)) ParseBool(inf, "infinite");
            foreach (var k in ArchNumberKeys) if (options.ContainsKey(k)) Number(options[k], k);
        }

        foreach (var raw in parts[0].Split(','))
        {
            string name = raw.Trim();
            if (name.Length == 0) continue;

            var p = new Parsed
            {
                Name = name, Group = groupAsset, Intrinsic = template.Intrinsic, From = template.From, Element = template.Element,
                Behavior = template.Behavior, Delivery = template.Delivery, Pool = template.Pool, ClearPool = template.ClearPool
            };
            foreach (var kv in template.Options) p.Options[kv.Key] = kv.Value;

            // An existing item with this name is updated where it lives; otherwise a new file.
            ScriptableObject existing = ExistingFor(name);
            p.Exists = existing != null;
            p.Path = p.Exists ? AssetDatabase.GetAssetPath(existing) : NewPath(groupAsset, name);
            yield return p;
        }
    }

    private string GroupWord => kind == Kind.Armor ? "slot" : kind == Kind.Weapon ? "archetype" : "type";

    private ScriptableObject ExistingFor(string name)
    {
        switch (kind)
        {
            case Kind.Armor: return armors.TryGetValue(Key(name), out var a) ? a : null;
            case Kind.Weapon: return weapons.TryGetValue(Key(name), out var w) ? w : null;
            default: return archetypes.TryGetValue(Key(name), out var r) ? r : null;
        }
    }

    // "damage=2 from=Plinker 300" -> {damage: 2, from: Plinker 300}
    private Dictionary<string, string> ParseOptions(string s)
    {
        var result = new Dictionary<string, string>();
        s = s.Trim();
        if (s.Length == 0) return result;

        var allowed = kind == Kind.Armor ? ArmorKeys : kind == Kind.Weapon ? WeaponKeys : ArchKeys;
        var matches = OptionPattern.Matches(s);
        if (matches.Count == 0 || matches[0].Index != 0) throw new FormatException($"expected key=value, got '{s}'");
        foreach (Match m in matches)
        {
            string key = m.Groups[1].Value.ToLowerInvariant();
            if (!allowed.Contains(key)) throw new FormatException($"unknown key '{key}'");
            if (result.ContainsKey(key)) throw new FormatException($"'{key}' given twice");
            result[key] = m.Groups[2].Value.Trim();
        }
        return result;
    }

    private static T Find<T>(Dictionary<string, T> map, string name, string what)
    {
        if (!map.TryGetValue(Key(name), out var v)) throw new FormatException($"unknown {what} '{name}'");
        return v;
    }

    private static float Number(string s, string key)
    {
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            throw new FormatException($"{key}: '{s}' is not a number");
        return v;
    }

    private static bool ParseBool(string s, string key)
    {
        switch (Key(s))
        {
            case "true": case "yes": case "on": case "1": return true;
            case "false": case "no": case "off": case "0": return false;
            default: throw new FormatException($"{key}: '{s}' is not true or false");
        }
    }

    private static ReloadStyle ParseReloadStyle(string s)
    {
        switch (Key(s))
        {
            case "magazine": case "mag": return ReloadStyle.Magazine;
            case "pershell": case "shell": return ReloadStyle.PerShell;
            default: throw new FormatException($"reloadstyle: '{s}' is not magazine or pershell");
        }
    }

    // ------------------------------------------------------------------ apply

    private void Apply()
    {
        int created = 0, updated = 0;
        foreach (var p in parsed)
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(p.Path).Replace('\\', '/'));
            ScriptableObject asset;
            if (kind == Kind.Armor) asset = ApplyArmor(p);
            else if (kind == Kind.Weapon) asset = ApplyWeapon(p);
            else asset = ApplyArchetype(p);

            if (p.Exists) { EditorUtility.SetDirty(asset); updated++; }
            else { AssetDatabase.CreateAsset(asset, p.Path); created++; }
        }
        AssetDatabase.SaveAssets();
        ClearLookups();
        Reparse();
        Debug.Log($"[ItemBatch] {kind}: {created} created, {updated} updated.");
    }

    private static T LoadOrCreate<T>(Parsed p) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(p.Path);
        if (a == null) return CreateInstance<T>();
        Undo.RecordObject(a, "Item Batch");
        return a;
    }

    private static ScriptableObject ApplyArmor(Parsed p)
    {
        var a = LoadOrCreate<ArmorSO>(p);
        a.displayName = p.Name;
        if (string.IsNullOrEmpty(a.id)) a.id = Id(p.Name);
        a.slot = (ArmorSlotSO)p.Group;
        if (p.Intrinsic != null) a.intrinsicPerks = new List<PerkSO>(p.Intrinsic);
        return a;
    }

    private static ScriptableObject ApplyWeapon(Parsed p)
    {
        var w = LoadOrCreate<WeaponSO>(p);
        var arch = (WeaponArchetypeSO)p.Group;
        w.displayName = p.Name;
        if (string.IsNullOrEmpty(w.id)) w.id = Id(p.Name);
        w.archetype = arch;

        // from= first, so explicit keys on the line win.
        if (p.From is WeaponSO src && src != w)
        {
            w.viewmodel = src.viewmodel;
            w.viewmodelSkin = new List<MaterialSwap>(src.viewmodelSkin);
            w.defaultShader = src.defaultShader;
            w.baseDamageType = src.baseDamageType;
            w.fireClip = src.fireClip;
            w.emptyClip = src.emptyClip;
            w.reloadClip = src.reloadClip;
            w.startingReserves = src.startingReserves;
            w.infiniteReservesOverride = src.infiniteReservesOverride;
        }
        else if (!p.Exists && arch.weaponType != null)
        {
            w.viewmodel = arch.weaponType.defaultViewmodel;
            w.defaultShader = arch.weaponType.defaultShader;
        }

        var o = p.Options;
        if (o.TryGetValue("damage", out var v)) w.weaponDamageDelta = Number(v, "damage");
        if (o.TryGetValue("rpm", out v)) w.roundsPerMinuteDelta = Number(v, "rpm");
        if (o.TryGetValue("mag", out v)) w.magazineSizeDelta = Number(v, "mag");
        if (o.TryGetValue("reload", out v)) w.reloadTimeDelta = Number(v, "reload");
        if (o.TryGetValue("handling", out v)) w.handlingDelta = Number(v, "handling");
        if (o.TryGetValue("zoom", out v)) w.adsZoomDelta = Number(v, "zoom");
        if (o.TryGetValue("range", out v)) w.rangeDelta = Number(v, "range");
        if (o.TryGetValue("aa", out v)) w.aimAssistDelta = Number(v, "aa");
        if (o.TryGetValue("reserves", out v)) w.startingReserves = Mathf.RoundToInt(Number(v, "reserves"));
        if (p.Element != null) w.baseDamageType = p.Element;
        if (p.Intrinsic != null) w.intrinsicPerks = new List<PerkSO>(p.Intrinsic);
        return w;
    }

    private static ScriptableObject ApplyArchetype(Parsed p)
    {
        var a = LoadOrCreate<WeaponArchetypeSO>(p);

        // from= copies every serialized field; name, id and type are set again after.
        if (p.From is WeaponArchetypeSO src && src != a)
        {
            string keepId = p.Exists ? a.id : "";
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(src), a);
            a.name = System.IO.Path.GetFileNameWithoutExtension(p.Path);
            a.id = keepId;
        }

        a.displayName = p.Name;
        if (string.IsNullOrEmpty(a.id)) a.id = Id(p.Name);
        a.weaponType = (WeaponTypeSO)p.Group;

        var o = p.Options;
        if (o.TryGetValue("damage", out var v)) a.weaponDamage = Number(v, "damage");
        if (o.TryGetValue("rpm", out v)) a.roundsPerMinute = Number(v, "rpm");
        if (o.TryGetValue("mag", out v)) a.magazineSize = Number(v, "mag");
        if (o.TryGetValue("reload", out v)) a.reloadTime = Number(v, "reload");
        if (o.TryGetValue("handling", out v)) a.handling = Number(v, "handling");
        if (o.TryGetValue("zoom", out v)) a.adsZoom = Number(v, "zoom");
        if (o.TryGetValue("range", out v)) a.range = Mathf.Clamp(Number(v, "range"), 0f, 100f);
        if (o.TryGetValue("aa", out v)) a.aimAssist = Mathf.Clamp(Number(v, "aa"), 0f, 100f);
        if (o.TryGetValue("ads", out v)) a.baseAdsTime = Number(v, "ads");
        if (o.TryGetValue("equip", out v)) a.baseEquipTime = Number(v, "equip");
        if (o.TryGetValue("stow", out v)) a.baseStowTime = Number(v, "stow");
        if (o.TryGetValue("reloadstyle", out v)) a.reloadStyle = ParseReloadStyle(v);
        if (o.TryGetValue("infinite", out v)) a.infiniteReserves = ParseBool(v, "infinite");

        if (a.primaryFire == null) a.primaryFire = new FireMode();
        if (p.Behavior != null) a.primaryFire.behavior = p.Behavior;
        if (p.Delivery != null) a.primaryFire.delivery = p.Delivery;
        if (p.Pool != null) a.ammoPool = p.Pool;
        if (p.ClearPool) a.ammoPool = null;
        return a;
    }

    // ------------------------------------------------------------------ load

    // Existing items back into lines, only non-default fields.
    private void Load()
    {
        var sb = new StringBuilder();
        if (kind == Kind.Armor)
        {
            foreach (var a in FindAll<ArmorSO>(ArmorRoot))
            {
                sb.Append(NameOf(a.displayName, a)).Append(" | ").Append(a.slot != null ? a.slot.name : "?");
                AppendIntrinsic(sb, a.intrinsicPerks);
                sb.Append('\n');
            }
        }
        else if (kind == Kind.Weapon)
        {
            foreach (var w in FindAll<WeaponSO>(WeaponRoot))
            {
                sb.Append(NameOf(w.displayName, w)).Append(" | ").Append(w.archetype != null ? w.archetype.name : "?").Append(" |");
                Delta(sb, "damage", w.weaponDamageDelta);
                Delta(sb, "rpm", w.roundsPerMinuteDelta);
                Delta(sb, "mag", w.magazineSizeDelta);
                Delta(sb, "reload", w.reloadTimeDelta);
                Delta(sb, "handling", w.handlingDelta);
                Delta(sb, "zoom", w.adsZoomDelta);
                Delta(sb, "range", w.rangeDelta);
                Delta(sb, "aa", w.aimAssistDelta);
                sb.Append(" reserves=").Append(w.startingReserves);
                if (w.baseDamageType != null) sb.Append(" element=").Append(NameOf(w.baseDamageType.displayName, w.baseDamageType));
                AppendIntrinsic(sb, w.intrinsicPerks);
                sb.Append('\n');
            }
        }
        else
        {
            foreach (var a in FindAll<WeaponArchetypeSO>(WeaponRoot))
            {
                sb.Append(NameOf(a.displayName, a)).Append(" | ").Append(a.weaponType != null ? a.weaponType.name : "?").Append(" |");
                Val(sb, "damage", a.weaponDamage); Val(sb, "rpm", a.roundsPerMinute); Val(sb, "mag", a.magazineSize);
                Val(sb, "reload", a.reloadTime); Val(sb, "handling", a.handling); Val(sb, "zoom", a.adsZoom);
                Val(sb, "range", a.range); Val(sb, "aa", a.aimAssist); Val(sb, "ads", a.baseAdsTime);
                Val(sb, "equip", a.baseEquipTime); Val(sb, "stow", a.baseStowTime);
                if (a.primaryFire != null && a.primaryFire.behavior != null) sb.Append(" behavior=").Append(a.primaryFire.behavior.name);
                if (a.primaryFire != null && a.primaryFire.delivery != null) sb.Append(" delivery=").Append(a.primaryFire.delivery.name);
                if (a.reloadStyle == ReloadStyle.PerShell) sb.Append(" reloadstyle=pershell");
                if (a.infiniteReserves) sb.Append(" infinite=true");
                if (a.ammoPool != null) sb.Append(" pool=").Append(a.ammoPool.name);
                sb.Append('\n');
            }
        }
        text = sb.ToString();
        GUI.FocusControl(null);
        Reparse();
    }

    private static void Delta(StringBuilder sb, string key, float v)
    {
        if (!Mathf.Approximately(v, 0f)) Val(sb, key, v);
    }

    private static void Val(StringBuilder sb, string key, float v)
        => sb.Append(' ').Append(key).Append('=').Append(v.ToString(CultureInfo.InvariantCulture));

    private static void AppendIntrinsic(StringBuilder sb, List<PerkSO> list)
    {
        if (list == null || list.Count == 0) return;
        sb.Append(" | intrinsic=");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(list[i] != null ? NameOf(list[i].displayName, list[i]) : "-");
        }
    }

    // ------------------------------------------------------------------ lookups

    private void ClearLookups()
    {
        slots = null; types = null; archetypes = null; perks = null; weapons = null;
        armors = null; elements = null; behaviors = null; deliveries = null; pools = null;
    }

    private void EnsureLookups()
    {
        if (slots == null) slots = Index<ArmorSlotSO>(s => new[] { s.id, s.displayName, s.name });
        if (types == null) types = Index<WeaponTypeSO>(t => new[] { t.id, t.displayName, t.name });
        if (archetypes == null) archetypes = Index<WeaponArchetypeSO>(a => new[] { a.id, a.displayName, a.name });
        if (perks == null) perks = Index<PerkSO>(p => new[] { p.id, p.displayName, p.name });
        if (weapons == null) weapons = Index<WeaponSO>(w => new[] { w.id, w.displayName, w.name });
        if (armors == null) armors = Index<ArmorSO>(a => new[] { a.id, a.displayName, a.name });
        if (elements == null) elements = Index<DamageTypeSO>(d => new[] { d.id, d.displayName, d.name });
        if (behaviors == null) behaviors = Index<FireBehaviorSO>(b => new[] { b.name });
        if (deliveries == null) deliveries = Index<DeliverySO>(d => new[] { d.name });
        if (pools == null) pools = Index<AmmoPoolSO>(p => new[] { p.id, p.displayName, p.name });
    }

    // Every asset of T by the given names, normalised. Slots also answer without "slot": "head" for HeadSlot.
    private static Dictionary<string, T> Index<T>(Func<T, string[]> keys) where T : ScriptableObject
    {
        var map = new Dictionary<string, T>();
        foreach (var asset in FindAll<T>(null))
            foreach (var k in keys(asset))
                if (!string.IsNullOrWhiteSpace(k) && !map.ContainsKey(Key(k))) map[Key(k)] = asset;

        if (typeof(T) == typeof(ArmorSlotSO))
            foreach (var kv in new List<KeyValuePair<string, T>>(map))
                if (kv.Key.EndsWith("slot") && kv.Key.Length > 4 && !map.ContainsKey(kv.Key.Substring(0, kv.Key.Length - 4)))
                    map[kv.Key.Substring(0, kv.Key.Length - 4)] = kv.Value;
        return map;
    }

    private static List<T> FindAll<T>(string folder) where T : ScriptableObject
    {
        var list = new List<T>();
        var guids = folder != null && AssetDatabase.IsValidFolder(folder)
            ? AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder })
            : AssetDatabase.FindAssets("t:" + typeof(T).Name);
        foreach (var guid in guids)
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) list.Add(a);
        }
        return list;
    }

    // ------------------------------------------------------------------ paths and names

    private static string NewPath(UnityEngine.Object group, string name)
    {
        string file = FileName(name);
        if (group is ArmorSlotSO slot)
        {
            string n = NameOf(slot.displayName, slot);
            if (n.EndsWith("Slot", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4).Trim();
            return $"{ArmorRoot}/{FileName(n)}/{file}.asset";
        }
        if (group is WeaponTypeSO type)
            return $"{WeaponRoot}/{type.name}/_Archetypes/{file}/{file}.asset";

        string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(group)).Replace('\\', '/');
        return $"{dir}/_Weapons/{file}.asset";
    }

    private static string NameOf(string display, UnityEngine.Object o) => string.IsNullOrWhiteSpace(display) ? o.name : display;

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
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        return s.Replace(" ", "");
    }

    private static string Id(string name) => name.Trim().ToLowerInvariant().Replace(' ', '_');

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
