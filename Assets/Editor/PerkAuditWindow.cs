using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Combat.Armor;
using Combat.Core;
using Combat.Events;
using Combat.Info;
using Combat.Loadout;
using Combat.Perks;
using Combat.Status;
using Combat.Weapons;
using Object = UnityEngine.Object;

// Edit-mode audit of every perk: bad data, triggers that can't fire, perks no item can roll, duplicate behaviour.
public class PerkAuditWindow : EditorWindow
{
    private enum Severity { Error, Warning, Info, Ok }

    private sealed class Row
    {
        public Severity Sev;
        public Object Asset;
        public string Name, Type, Issue;
        public int Rolls, Intrinsic;
    }

    // Where a perk can show up
    private sealed class Usage
    {
        public readonly HashSet<string> RollOn = new HashSet<string>();
        public readonly HashSet<string> IntrinsicOn = new HashSet<string>();
        public readonly HashSet<string> CuratedOn = new HashSet<string>();
    }

    private readonly List<Row> rows = new List<Row>();
    private readonly Dictionary<PerkSO, Usage> usage = new Dictionary<PerkSO, Usage>();
    private Vector2 scroll;
    private string search = "";
    private bool showErrors = true, showWarnings = true, showInfo = true, showOk = false;
    private int perkCount, poolCount, itemCount;
    private double lastRun;

    private static readonly GUIContent[] SevIcons = new GUIContent[4];

    [MenuItem("FPS0/Items & Perks/Perk Audit", priority = 530)]
    private static void Open() => GetWindow<PerkAuditWindow>("Perk Audit");

    private void OnEnable() => Run();

    // ------------------------------------------------------------------ GUI

    private void OnGUI()
    {
        if (SevIcons[0] == null)
        {
            SevIcons[0] = EditorGUIUtility.IconContent("console.erroricon.sml");
            SevIcons[1] = EditorGUIUtility.IconContent("console.warnicon.sml");
            SevIcons[2] = EditorGUIUtility.IconContent("console.infoicon.sml");
            SevIcons[3] = EditorGUIUtility.IconContent("TestPassed");
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Run Audit", EditorStyles.toolbarButton, GUILayout.Width(80))) Run();
            showErrors = GUILayout.Toggle(showErrors, $" {Count(Severity.Error)}", EditorStyles.toolbarButton, GUILayout.Width(50));
            showWarnings = GUILayout.Toggle(showWarnings, $" {Count(Severity.Warning)}", EditorStyles.toolbarButton, GUILayout.Width(50));
            showInfo = GUILayout.Toggle(showInfo, $" {Count(Severity.Info)}", EditorStyles.toolbarButton, GUILayout.Width(50));
            showOk = GUILayout.Toggle(showOk, $"OK {Count(Severity.Ok)}", EditorStyles.toolbarButton, GUILayout.Width(60));
            GUILayout.Space(8);
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy Shown", EditorStyles.toolbarButton, GUILayout.Width(80)))
                Copy(Report(true));
            if (GUILayout.Button("Copy All Problems", EditorStyles.toolbarButton, GUILayout.Width(115)))
                Copy(Report(false));
        }

        EditorGUILayout.LabelField($"{perkCount} perks, {poolCount} pools, {itemCount} items scanned" +
            (lastRun > 0 ? $"  ({DateTime.Now.AddSeconds(lastRun - EditorApplication.timeSinceStartup):HH:mm:ss})" : ""), EditorStyles.miniLabel);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("", GUILayout.Width(20));
            GUILayout.Label("Perk", GUILayout.Width(160));
            GUILayout.Label("Type", GUILayout.Width(120));
            GUILayout.Label("Rolls", GUILayout.Width(40));
            GUILayout.Label("Intr", GUILayout.Width(32));
            GUILayout.Label("Issue");
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var r in rows)
        {
            if (!Visible(r)) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(SevIcons[(int)r.Sev], GUILayout.Width(20), GUILayout.Height(18));
                if (GUILayout.Button(r.Name, EditorStyles.linkLabel, GUILayout.Width(160)) && r.Asset != null)
                {
                    EditorGUIUtility.PingObject(r.Asset);
                    Selection.activeObject = r.Asset;
                }
                GUILayout.Label(r.Type, EditorStyles.miniLabel, GUILayout.Width(120));
                GUILayout.Label(r.Rolls >= 0 ? r.Rolls.ToString() : "-", GUILayout.Width(40));
                GUILayout.Label(r.Intrinsic >= 0 ? r.Intrinsic.ToString() : "-", GUILayout.Width(32));
                GUILayout.Label(r.Issue, EditorStyles.wordWrappedLabel);
            }
            var rect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition))
            {
                var row = r;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy Row"), false, () => Copy(Line(row)));
                menu.AddItem(new GUIContent("Copy Rows For This Perk"), false, () => Copy(string.Join("\n", rows.Where(x => x.Asset == row.Asset && x.Sev != Severity.Ok).Select(Line))));
                menu.ShowAsContext();
                Event.current.Use();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private int Count(Severity s) => rows.Count(r => r.Sev == s);

    private bool Visible(Row r)
    {
        if (r.Sev == Severity.Error && !showErrors) return false;
        if (r.Sev == Severity.Warning && !showWarnings) return false;
        if (r.Sev == Severity.Info && !showInfo) return false;
        if (r.Sev == Severity.Ok && !showOk) return false;
        if (string.IsNullOrEmpty(search)) return true;
        string s = search.ToLowerInvariant();
        return (r.Name ?? "").ToLowerInvariant().Contains(s) || (r.Issue ?? "").ToLowerInvariant().Contains(s) || (r.Type ?? "").ToLowerInvariant().Contains(s);
    }

    // Shown = current filters and search; otherwise every non-OK row
    private string Report(bool shownOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Perk Audit: {Count(Severity.Error)} errors, {Count(Severity.Warning)} warnings, {Count(Severity.Info)} info");
        foreach (var r in rows)
            if (r.Sev != Severity.Ok && (!shownOnly || Visible(r))) sb.AppendLine(Line(r));
        return sb.ToString();
    }

    private static string Line(Row r) => $"[{r.Sev}] {r.Name} ({r.Type}, rolls {r.Rolls}, intrinsic {r.Intrinsic}): {r.Issue}";

    private void Copy(string text)
    {
        EditorGUIUtility.systemCopyBuffer = text;
        ShowNotification(new GUIContent("Copied"), 1.0);
    }

    // ------------------------------------------------------------------ run

    private void Run()
    {
        // Pick up files changed outside Unity before reading them
        AssetDatabase.Refresh();
        rows.Clear();
        usage.Clear();
        armorNames = null;

        var perks = Load<PerkSO>();
        var pools = Load<PerkPoolSO>();
        var weapons = Load<WeaponSO>();
        var armors = Load<ArmorSO>();
        perkCount = perks.Count; poolCount = pools.Count; itemCount = weapons.Count + armors.Count;

        foreach (var p in perks) usage[p] = new Usage();
        var usedPools = new HashSet<PerkPoolSO>();
        BuildUsage(weapons, armors, usedPools);

        CheckIds(perks);
        CheckFamilies(perks);
        CheckDuplicateBehaviour(perks);
        foreach (var p in perks) CheckPerk(p);
        CheckPools(pools, usedPools);

        foreach (var p in perks)
            if (!rows.Any(r => r.Asset == p && r.Sev != Severity.Ok && r.Sev != Severity.Info))
                Add(Severity.Ok, p, "No problems found.");

        rows.Sort((a, b) => a.Sev != b.Sev ? a.Sev.CompareTo(b.Sev) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        lastRun = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private static List<T> Load<T>() where T : Object
    {
        var list = new List<T>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null && !list.Contains(a)) list.Add(a);
        }
        return list;
    }

    // Which items can roll each perk (element filtered) or carry it as intrinsic or curated
    private void BuildUsage(List<WeaponSO> weapons, List<ArmorSO> armors, HashSet<PerkPoolSO> usedPools)
    {
        var gathered = new List<PerkSO>();
        foreach (var w in weapons)
        {
            string n = w.name;
            foreach (var p in w.ResolvedIntrinsics) Use(p)?.IntrinsicOn.Add(n);
            foreach (var p in w.curatedRoll) Use(p)?.CuratedOn.Add(n);
            var cols = w.ResolvedPerkColumns;
            if (cols == null) continue;
            foreach (var c in cols)
            {
                if (c == null) continue;
                MarkPools(c, usedPools);
                c.Gather(gathered, w.baseDamageType);
                foreach (var p in gathered)
                    if (p != null && PerkRoll.FitsElement(p, w.baseDamageType)) Use(p)?.RollOn.Add(n);
            }
        }
        foreach (var a in armors)
        {
            string n = a.name;
            foreach (var p in a.intrinsicPerks) Use(p)?.IntrinsicOn.Add(n);
            foreach (var p in a.curatedRoll) Use(p)?.CuratedOn.Add(n);
            var cols = a.ResolvedPerkColumns;
            if (cols == null) continue;
            foreach (var c in cols)
            {
                if (c == null) continue;
                MarkPools(c, usedPools);
                c.Gather(gathered, null);
                foreach (var p in gathered)
                    if (p != null && PerkRoll.FitsElement(p, null)) Use(p)?.RollOn.Add(n);
            }
        }
    }

    private Usage Use(PerkSO p) => p != null && usage.TryGetValue(p, out var u) ? u : null;

    private static void MarkPools(PerkColumn c, HashSet<PerkPoolSO> used)
    {
        Mark(c.source, used);
        if (c.byElement != null) foreach (var e in c.byElement) if (e != null) Mark(e.pool, used);
    }

    private static void Mark(PerkPoolSO pool, HashSet<PerkPoolSO> used)
    {
        if (pool == null || !used.Add(pool)) return;
        foreach (var inc in pool.includes) Mark(inc, used);
    }

    // ------------------------------------------------------------------ checks: whole set

    private void CheckIds(List<PerkSO> perks)
    {
        foreach (var g in perks.Where(p => !string.IsNullOrWhiteSpace(p.id)).GroupBy(p => p.id.Trim()))
            if (g.Count() > 1)
                foreach (var p in g) Add(Severity.Error, p, $"Duplicate id '{g.Key}' (also on {Others(g, p)}).");
    }

    // Same family name and tier on one item: the roll treats them as one perk
    private void CheckFamilies(List<PerkSO> perks)
    {
        foreach (var g in perks.GroupBy(p => PerkRoll.Family(p) + "|" + PerkRoll.Tier(p)))
            if (g.Count() > 1) Pairs(g.ToList(), "Same name and tier as {0}; rolls treat them as one family");
    }

    // Intrinsics must be unique everywhere; other perks only warn when one item can have both
    private void CheckDuplicateBehaviour(List<PerkSO> perks)
    {
        foreach (var g in perks.GroupBy(Signature))
        {
            var group = g.ToList();
            if (group.Count < 2) continue;
            foreach (var p in group)
            {
                var u = Use(p);
                if (u == null || u.IntrinsicOn.Count == 0) continue;
                Add(Severity.Error, p, $"Intrinsic is not unique: behaves exactly like {Others(group, p)}.");
            }
            Pairs(group.Where(p => Use(p) == null || Use(p).IntrinsicOn.Count == 0).ToList(), "Behaves exactly like {0}; both can be on the same item");
        }
    }

    // Warns once per perk for each other perk in the group that shares an item with it
    private void Pairs(List<PerkSO> group, string format)
    {
        foreach (var p in group)
        {
            var mine = Items(p);
            var hits = new List<string>();
            foreach (var q in group)
            {
                if (q == p) continue;
                var shared = mine.Intersect(Items(q)).ToList();
                if (shared.Count > 0) hits.Add($"{q.name} ({shared.Count} items, e.g. {shared[0]})");
            }
            if (hits.Count > 0) Add(Severity.Warning, p, string.Format(format, string.Join(", ", hits)) + ".");
        }
    }

    private HashSet<string> Items(PerkSO p)
    {
        var set = new HashSet<string>();
        var u = Use(p);
        if (u == null) return set;
        set.UnionWith(u.RollOn); set.UnionWith(u.IntrinsicOn); set.UnionWith(u.CuratedOn);
        return set;
    }

    private static string Signature(PerkSO p)
    {
        string json = EditorJsonUtility.ToJson(p);
        foreach (var key in new[] { "id", "displayName", "description", "customDescription", "shortName", "icon", "m_Name" })
            json = System.Text.RegularExpressions.Regex.Replace(json, "\"" + key + "\":(\"(\\\\.|[^\"\\\\])*\"|\\{[^}]*\\}|[^,}]*),?", "");
        return p.GetType().Name + json;
    }

    private static string Others(IEnumerable<PerkSO> group, PerkSO self)
        => string.Join(", ", group.Where(x => x != self).Select(x => x.name));

    private void CheckPools(List<PerkPoolSO> pools, HashSet<PerkPoolSO> used)
    {
        foreach (var pool in pools)
        {
            int nulls = pool.perks.Count(p => p == null) + pool.includes.Count(i => i == null);
            if (nulls > 0) Add(Severity.Warning, pool, $"{nulls} empty entries.", "Pool");
            if (!used.Contains(pool)) Add(Severity.Info, pool, "No roll column uses this pool or a pool that includes it.", "Pool");
        }
    }

    // ------------------------------------------------------------------ checks: one perk

    private void CheckPerk(PerkSO p)
    {
        if (string.IsNullOrWhiteSpace(p.id)) Add(Severity.Warning, p, "No id.");
        if (string.IsNullOrWhiteSpace(p.displayName)) Add(Severity.Warning, p, "No display name.");

        CheckDescription(p);
        CheckUsage(p);
        CheckModifiers(p);
        CheckTriggers(p);
        CheckSpecific(p);
    }

    private void CheckDescription(PerkSO p)
    {
        try
        {
            string text = Describer.Build(p, new DescribeContext { Rich = false });
            if (string.IsNullOrWhiteSpace(text)) Add(Severity.Warning, p, "Description is empty.");
            else if (text.Contains("{") || text.Contains("}")) Add(Severity.Warning, p, $"Description has an unfilled token: \"{text}\".");
        }
        catch (Exception e)
        {
            Add(Severity.Error, p, "Description threw: " + e.Message);
        }
    }

    private void CheckUsage(PerkSO p)
    {
        var u = Use(p);
        if (u == null) return;
        if (u.RollOn.Count == 0 && u.IntrinsicOn.Count == 0 && u.CuratedOn.Count == 0)
        {
            var elems = new List<DamageTypeSO>();
            p.CollectElements(elems);
            string why = elems.Count > 0 ? $" It needs a {string.Join("/", elems.Where(e => e != null).Select(e => e.name))} weapon." : "";
            Add(Severity.Warning, p, "No item can roll it or carries it." + why);
        }
    }

    private void CheckModifiers(PerkSO p)
    {
        foreach (var f in Fields(p))
        {
            if (!(f.GetValue(p) is List<ModifierEntry> list)) continue;
            bool buff = f.Name == "modifiers" && (p is TriggeredBuffPerkSO || p is ArmedBuffPerkSO);
            if (list.Count == 0 && buff) Add(Severity.Error, p, "Buff has no modifiers, so it does nothing.");
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || m.stat == null) { Add(Severity.Error, p, $"{f.Name}[{i}] has no stat."); continue; }
                if (Mathf.Approximately(m.value, 0f)) Add(Severity.Warning, p, $"{f.Name}[{i}] ({m.stat.name}) has value 0.");
                string path = AssetDatabase.GetAssetPath(m.stat).Replace('\\', '/');
                if (path.Contains("/Definitions/Weapon/") && m.target == ModifierTarget.Player)
                    Add(Severity.Error, p, $"{m.stat.name} is a weapon stat but targets Player.");
                else if ((path.Contains("/Definitions/Player/") || path.Contains("/Definitions/Movement/")) && m.target == ModifierTarget.Weapons)
                    Add(Severity.Error, p, $"{m.stat.name} is a player stat but targets Weapons.");
                else if (path.Contains("/Definitions/Enemy AI/"))
                    Add(Severity.Warning, p, $"{m.stat.name} is an enemy-side stat; a perk modifier on it likely does nothing.");
            }
        }
    }

    private static readonly HashSet<WeaponEventType> HitFamily = new HashSet<WeaponEventType>
        { WeaponEventType.Hit, WeaponEventType.PrecisionHit, WeaponEventType.Kill, WeaponEventType.PrecisionKill, WeaponEventType.DamageTaken };

    private static readonly HashSet<WeaponEventType> OwnWeaponOnly = new HashSet<WeaponEventType>
        { WeaponEventType.ShotFired, WeaponEventType.DryFire, WeaponEventType.ReloadStart, WeaponEventType.ReloadComplete,
          WeaponEventType.ReloadCancelled, WeaponEventType.MagEmpty, WeaponEventType.MagFull };

    private void CheckTriggers(PerkSO p)
    {
        bool armor = usage.TryGetValue(p, out var u) && u.RollOn.Count + u.IntrinsicOn.Count + u.CuratedOn.Count > 0
                     && u.RollOn.Concat(u.IntrinsicOn).Concat(u.CuratedOn).All(IsArmorName);
        bool anyWeapon = Get(p, "anyWeapon", false);
        bool oncePerShot = Get(p, "oncePerShot", false);
        int everyN = Get(p, "everyN", 1);
        float window = Get(p, "window", 0f);

        foreach (var (field, mask) in new[] { ("trigger", "sources"), ("armTrigger", "armSources"), ("spendTrigger", "spendSources") })
        {
            if (!Has(p, field)) continue;
            var t = Get(p, field, WeaponEventType.Hit);
            var m = Get(p, mask, HitSourceMask.Direct);

            if (HitFamily.Contains(t) && Has(p, mask) && m == HitSourceMask.None)
                Add(Severity.Error, p, $"{field} {t} with no hit sources selected; it never fires.");

            if (!armor && p.listenScope == PerkListenScope.StowedOnly && OwnWeaponOnly.Contains(t) && !anyWeapon)
                Add(Severity.Error, p, $"Listens only while stowed, but {t} comes from its own weapon, which can't do that while stowed.");

            if (!armor && p.listenScope == PerkListenScope.StowedOnly && HitFamily.Contains(t) && t != WeaponEventType.DamageTaken
                && !anyWeapon && (m & (HitSourceMask.StatusTick | HitSourceMask.Chain)) == 0)
                Add(Severity.Error, p, $"Listens only while stowed, but {t} from its own direct shots can't happen while stowed.");

            if (!armor && p.listenScope == PerkListenScope.Active && t == WeaponEventType.Stow)
                Add(Severity.Warning, p, "Triggers on Stow but only listens while held; check it fires.");

            if (!armor && p.listenScope == PerkListenScope.Active && anyWeapon)
                Add(Severity.Warning, p, "Hears every weapon but only while this one is held, so other weapons rarely reach it.");

            if (oncePerShot && field == "trigger" && !HitFamily.Contains(t))
                Add(Severity.Info, p, $"Once Per Shot is ignored for {t}.");
        }

        if (everyN > 1 && window > 0f && window < 0.05f * (everyN - 1))
            Add(Severity.Warning, p, $"{everyN} triggers within {window:0.##} s is likely too tight to hit.");

        if (p is TriggeredBuffPerkSO tb)
        {
            if (tb.charges > 0 && tb.trigger == WeaponEventType.ShotFired)
                Add(Severity.Info, p, "Charges are ignored when the trigger is Shot Fired.");
            if (tb.duration <= 0f && tb.charges <= 0 && !tb.endOnStow)
                Add(Severity.Warning, p, "No duration, charges or end on stow: the buff never ends.");
        }
        if (p is ArmedBuffPerkSO ab && ab.duration <= 0f && ab.charges <= 0 && !ab.endOnStow)
            Add(Severity.Warning, p, "No duration, charges or end on stow: the buff never ends.");
    }

    private void CheckSpecific(PerkSO p)
    {
        if (p is MagRefillPerkSO mr && mr.rounds <= 0 && mr.magFraction <= 0f)
            Add(Severity.Error, p, "Refills 0 rounds and 0% of the magazine.");

        if (p is StatusOnTriggerPerkSO st)
        {
            if (st.status == null) Add(Severity.Error, p, "No status assigned.");
            bool kill = st.trigger == WeaponEventType.Kill || st.trigger == WeaponEventType.PrecisionKill;
            if (kill && st.radius <= 0f) Add(Severity.Error, p, "Applies to the target on a kill; the target is already dead.");
            if (kill && st.radius > 0f && st.includeTarget) Add(Severity.Info, p, "Include Target is on for a kill trigger; the target is dead.");
            if (st.trigger == WeaponEventType.DamageTaken) Add(Severity.Error, p, "On Damage Taken the target is the player; this would apply the status to yourself.");
            if (st.radius > 0f && st.enemyLayers.value == 0) Add(Severity.Error, p, "Area with no enemy layers selected.");
        }

        if (p is HitConditionPerkSO hc)
        {
            string cond = Get(p, "condition", (object)null)?.ToString();
            if (cond == "TargetHasStatus" && (hc.statuses == null || hc.statuses.Count == 0 || hc.statuses.Any(s => s == null)))
                Add(Severity.Error, p, "Target Has Status with no status (or an empty entry).");
            if (Mathf.Approximately(hc.bonus, 0f)) Add(Severity.Warning, p, "Bonus is 0.");
        }

    }

    // ------------------------------------------------------------------ helpers

    private static HashSet<string> armorNames;
    private static bool IsArmorName(string n)
    {
        if (armorNames == null) armorNames = new HashSet<string>(Load<ArmorSO>().Select(a => a.name));
        return armorNames.Contains(n);
    }

    private static IEnumerable<FieldInfo> Fields(object o)
    {
        for (var t = o.GetType(); t != null && t != typeof(ScriptableObject); t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null) yield return f;
    }

    private static bool Has(object o, string name) => Fields(o).Any(f => f.Name == name);

    private static T Get<T>(object o, string name, T fallback)
    {
        var f = Fields(o).FirstOrDefault(x => x.Name == name);
        if (f == null) return fallback;
        var v = f.GetValue(o);
        return v is T t ? t : fallback;
    }

    private void Add(Severity sev, Object asset, string issue, string type = null)
    {
        var perk = asset as PerkSO;
        var u = perk != null ? Use(perk) : null;
        rows.Add(new Row
        {
            Sev = sev,
            Asset = asset,
            Name = perk != null && !string.IsNullOrWhiteSpace(perk.displayName) ? perk.displayName : asset != null ? asset.name : "?",
            Type = type ?? (asset != null ? asset.GetType().Name.Replace("PerkSO", "") : ""),
            Issue = issue,
            Rolls = u != null ? u.RollOn.Count : -1,
            Intrinsic = u != null ? u.IntrinsicOn.Count + u.CuratedOn.Count : -1
        });
    }

    private void OnDisable() => armorNames = null;
}
